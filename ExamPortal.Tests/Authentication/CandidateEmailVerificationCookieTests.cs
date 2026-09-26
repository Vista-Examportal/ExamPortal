using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Threading.Tasks;
using ExamPortal.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace ExamPortal.Tests.Authentication
{
    /// <summary>
    /// Covers the fix for: registration (AccountController.Registration.cs Register())
    /// used to call SignInUserAsync — the app's real, [Authorize]-recognized login
    /// cookie — before the candidate had verified their OTP, so a signed-in-but-
    /// unverified candidate could reach the dashboard directly. Register (and Login, for
    /// an existing unverified account) now call SignInPendingVerificationAsync instead,
    /// which signs into the separate AuthSchemes.PendingEmailVerification cookie scheme;
    /// only a successful VerifyEmailOtp calls the real SignInUserAsync.
    ///
    /// Same rationale/approach as CookieAuthenticationTimeoutTests in this folder: a
    /// full WebApplicationFactory&lt;Program&gt; host isn't viable here (Program.cs's
    /// DatabaseInitializer.EnsureSchema issues raw SQL Server T-SQL that EF Core's
    /// InMemory provider can't run), so this builds a small, isolated test host instead.
    /// It registers the *same two cookie schemes*, by the *same scheme name constant*
    /// (ExamPortal.Models.AuthSchemes.PendingEmailVerification) and default scheme, that
    /// Program.cs registers, with endpoints that mirror exactly what
    /// AccountController.Registration.cs/.Auth.cs now do:
    ///   - /simulate-register           → SignInPendingVerificationAsync only (Register)
    ///   - /simulate-login-unverified   → SignInPendingVerificationAsync only (Login's new branch)
    ///   - /simulate-verify-otp         → reads the pending cookie; on valid=true, signs
    ///                                     into the real scheme AND clears the pending one
    ///                                     (VerifyEmailOtp POST); on valid=false, does
    ///                                     neither (an invalid/expired OTP)
    ///   - /simulate-resend             → re-issues the pending cookie only if not already
    ///                                     on the real scheme (ResendEmailOtp)
    ///   - /dashboard                   → [Authorize(Roles=Candidate)]-equivalent: real
    ///                                     scheme + Candidate role only
    ///   - /verify-page                 → GetSignedInOrPendingVerificationCandidateAsync-
    ///                                     equivalent: real scheme OR pending scheme
    /// This exercises the real ASP.NET Core CookieAuthenticationHandler for both schemes
    /// (Set-Cookie issuance, scheme isolation, sign-out), not a re-implementation of it.
    /// </summary>
    public class CandidateEmailVerificationCookieTests
    {
        private const string RealCookieName = "TestPortalAuth";
        private const string PendingCookieName = "TestPendingVerification";
        private const string CandidateUserId = "42";

        private sealed class TestHostHandle : IDisposable
        {
            public required TestServer Server { get; init; }
            public required IHost UnderlyingHost { get; init; }
            public void Dispose() => UnderlyingHost.Dispose();
        }

        private static TestHostHandle BuildServer()
        {
            var hostBuilder = new HostBuilder()
                .ConfigureWebHost(webHost =>
                {
                    webHost.UseTestServer();
                    webHost.ConfigureServices(services =>
                    {
                        // Same EndpointDataSource workaround as CookieAuthenticationTimeoutTests
                        // — see that file's comment for why this is needed under the classic
                        // HostBuilder hosting model.
                        services.AddSingleton<EndpointDataSource>(new DefaultEndpointDataSource());
                        services.AddRouting();
                        services.AddAuthorization();
                        services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
                            .AddCookie(opt =>
                            {
                                opt.Cookie.Name = RealCookieName;
                            })
                            .AddCookie(AuthSchemes.PendingEmailVerification, opt =>
                            {
                                opt.Cookie.Name = PendingCookieName;
                                opt.ExpireTimeSpan = TimeSpan.FromMinutes(15);
                                opt.SlidingExpiration = false;
                            });
                    });
                    webHost.Configure(app =>
                    {
                        app.UseRouting();
                        app.UseAuthentication();
                        app.UseAuthorization();
                        app.UseEndpoints(endpoints =>
                        {
                            // Mirrors Register() / Login()'s new unverified branch:
                            // SignInPendingVerificationAsync only, never the real cookie.
                            endpoints.MapGet("/simulate-register", async context =>
                            {
                                var claims = new[] { new Claim(ClaimTypes.NameIdentifier, CandidateUserId) };
                                var identity = new ClaimsIdentity(claims, AuthSchemes.PendingEmailVerification);
                                await context.SignInAsync(AuthSchemes.PendingEmailVerification, new ClaimsPrincipal(identity));
                                context.Response.StatusCode = 200;
                            });
                            endpoints.MapGet("/simulate-login-unverified", async context =>
                            {
                                var claims = new[] { new Claim(ClaimTypes.NameIdentifier, CandidateUserId) };
                                var identity = new ClaimsIdentity(claims, AuthSchemes.PendingEmailVerification);
                                await context.SignInAsync(AuthSchemes.PendingEmailVerification, new ClaimsPrincipal(identity));
                                context.Response.StatusCode = 200;
                            });

                            // Mirrors VerifyEmailOtp POST: reads the pending cookie; a valid
                            // OTP signs into the real scheme and clears the pending one; an
                            // invalid/expired OTP does neither.
                            endpoints.MapGet("/simulate-verify-otp", async context =>
                            {
                                var pending = await context.AuthenticateAsync(AuthSchemes.PendingEmailVerification);
                                if (!pending.Succeeded || pending.Principal == null)
                                {
                                    context.Response.StatusCode = 401;
                                    return;
                                }

                                if (context.Request.Query["valid"] != "true")
                                {
                                    // Invalid/expired OTP — CandidateWorkflowService.VerifyEmailOtp
                                    // returns false, the controller returns the view again with a
                                    // ModelState error. Neither cookie changes.
                                    context.Response.StatusCode = 200;
                                    await context.Response.WriteAsync("otp-rejected");
                                    return;
                                }

                                var userId = pending.Principal.FindFirstValue(ClaimTypes.NameIdentifier)!;
                                var claims = new[]
                                {
                                    new Claim(ClaimTypes.NameIdentifier, userId),
                                    new Claim(ClaimTypes.Role, PortalRoles.Candidate),
                                };
                                var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
                                await context.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));
                                await context.SignOutAsync(AuthSchemes.PendingEmailVerification);
                                context.Response.StatusCode = 200;
                            });

                            // Mirrors ResendEmailOtp: re-issues the pending cookie only when
                            // not already on the real scheme.
                            endpoints.MapGet("/simulate-resend", async context =>
                            {
                                var pending = await context.AuthenticateAsync(AuthSchemes.PendingEmailVerification);
                                if (context.User.Identity?.IsAuthenticated != true && pending.Succeeded && pending.Principal != null)
                                {
                                    var userId = pending.Principal.FindFirstValue(ClaimTypes.NameIdentifier)!;
                                    var claims = new[] { new Claim(ClaimTypes.NameIdentifier, userId) };
                                    var identity = new ClaimsIdentity(claims, AuthSchemes.PendingEmailVerification);
                                    await context.SignInAsync(AuthSchemes.PendingEmailVerification, new ClaimsPrincipal(identity));
                                }
                                context.Response.StatusCode = 200;
                            });

                            // Mirrors [Authorize(Roles = PortalRoles.Candidate)] on
                            // HomeController/ExamController/etc: only the real (default)
                            // scheme counts — context.User here is populated purely from
                            // whatever UseAuthentication() auto-authenticated, i.e. the
                            // default scheme, exactly like production [Authorize].
                            endpoints.MapGet("/dashboard", context =>
                            {
                                context.Response.StatusCode =
                                    context.User.Identity?.IsAuthenticated == true && context.User.IsInRole(PortalRoles.Candidate)
                                        ? 200 : 401;
                                return Task.CompletedTask;
                            });

                            // Mirrors GetSignedInOrPendingVerificationCandidateAsync: real
                            // scheme first, else the pending scheme.
                            endpoints.MapGet("/verify-page", async context =>
                            {
                                if (context.User.Identity?.IsAuthenticated == true)
                                {
                                    context.Response.StatusCode = 200;
                                    return;
                                }
                                var pending = await context.AuthenticateAsync(AuthSchemes.PendingEmailVerification);
                                context.Response.StatusCode = pending.Succeeded ? 200 : 401;
                            });
                        });
                    });
                });

            var host = hostBuilder.Start();
            return new TestHostHandle { Server = host.GetTestServer(), UnderlyingHost = host };
        }

        private static string? ExtractCookieByName(HttpResponseMessage response, string cookieName)
        {
            if (!response.Headers.TryGetValues("Set-Cookie", out var values)) return null;
            foreach (var raw in values)
            {
                var semi = raw.IndexOf(';');
                var nameValue = semi >= 0 ? raw[..semi] : raw;
                var eq = nameValue.IndexOf('=');
                var name = eq >= 0 ? nameValue[..eq] : nameValue;
                if (string.Equals(name, cookieName, StringComparison.Ordinal))
                    return nameValue;
            }
            return null;
        }

        /// <summary>True if this response tells the browser to delete the named cookie
        /// (an Expires attribute in the past) — what HttpContext.SignOutAsync produces.</summary>
        private static bool CookieIsBeingCleared(HttpResponseMessage response, string cookieName)
        {
            if (!response.Headers.TryGetValues("Set-Cookie", out var values)) return false;
            foreach (var raw in values)
            {
                var parts = raw.Split(';', StringSplitOptions.TrimEntries);
                var nameValue = parts[0];
                var eq = nameValue.IndexOf('=');
                var name = eq >= 0 ? nameValue[..eq] : nameValue;
                if (!string.Equals(name, cookieName, StringComparison.Ordinal)) continue;

                foreach (var part in parts.Skip(1))
                {
                    if (part.StartsWith("expires=", StringComparison.OrdinalIgnoreCase)
                        && DateTimeOffset.TryParse(part["expires=".Length..], out var expires)
                        && expires < DateTimeOffset.UtcNow)
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        private static HttpRequestMessage RequestWithCookie(string path, string cookie) =>
            new(HttpMethod.Get, path) { Headers = { { "Cookie", cookie } } };

        // ── Registration must not create the real auth cookie ────────────────────

        [Fact]
        public async Task Register_IssuesOnlyThePendingVerificationCookie_NeverTheRealLoginCookie()
        {
            using var host = BuildServer();
            var client = host.Server.CreateClient();

            var response = await client.GetAsync("/simulate-register");

            Assert.NotNull(ExtractCookieByName(response, PendingCookieName));
            Assert.Null(ExtractCookieByName(response, RealCookieName));
        }

        [Fact]
        public async Task AfterRegister_DashboardIsNotAccessible()
        {
            using var host = BuildServer();
            var client = host.Server.CreateClient();

            var registerResponse = await client.GetAsync("/simulate-register");
            var pendingCookie = ExtractCookieByName(registerResponse, PendingCookieName);
            Assert.NotNull(pendingCookie);

            var dashboardResponse = await client.SendAsync(RequestWithCookie("/dashboard", pendingCookie!));

            Assert.Equal(HttpStatusCode.Unauthorized, dashboardResponse.StatusCode);
        }

        [Fact]
        public async Task AfterRegister_VerificationPageIsStillReachable()
        {
            using var host = BuildServer();
            var client = host.Server.CreateClient();

            var registerResponse = await client.GetAsync("/simulate-register");
            var pendingCookie = ExtractCookieByName(registerResponse, PendingCookieName);
            Assert.NotNull(pendingCookie);

            var verifyPageResponse = await client.SendAsync(RequestWithCookie("/verify-page", pendingCookie!));

            Assert.Equal(HttpStatusCode.OK, verifyPageResponse.StatusCode);
        }

        // ── Login for an existing unverified candidate behaves the same way ──────

        [Fact]
        public async Task LoginOfUnverifiedCandidate_AlsoIssuesOnlyThePendingCookie()
        {
            using var host = BuildServer();
            var client = host.Server.CreateClient();

            var response = await client.GetAsync("/simulate-login-unverified");

            Assert.NotNull(ExtractCookieByName(response, PendingCookieName));
            Assert.Null(ExtractCookieByName(response, RealCookieName));
        }

        // ── A valid OTP is what actually creates the real auth cookie ────────────

        [Fact]
        public async Task ValidOtp_IssuesTheRealLoginCookie_AndDashboardBecomesAccessible()
        {
            using var host = BuildServer();
            var client = host.Server.CreateClient();

            var registerResponse = await client.GetAsync("/simulate-register");
            var pendingCookie = ExtractCookieByName(registerResponse, PendingCookieName)!;

            var verifyResponse = await client.SendAsync(RequestWithCookie("/simulate-verify-otp?valid=true", pendingCookie));
            var realCookie = ExtractCookieByName(verifyResponse, RealCookieName);
            Assert.NotNull(realCookie);

            var dashboardResponse = await client.SendAsync(RequestWithCookie("/dashboard", realCookie!));
            Assert.Equal(HttpStatusCode.OK, dashboardResponse.StatusCode);
        }

        [Fact]
        public async Task ValidOtp_ClearsThePendingVerificationCookie()
        {
            using var host = BuildServer();
            var client = host.Server.CreateClient();

            var registerResponse = await client.GetAsync("/simulate-register");
            var pendingCookie = ExtractCookieByName(registerResponse, PendingCookieName)!;

            var verifyResponse = await client.SendAsync(RequestWithCookie("/simulate-verify-otp?valid=true", pendingCookie));

            Assert.True(CookieIsBeingCleared(verifyResponse, PendingCookieName));
        }

        // ── An invalid/expired OTP authenticates nobody ───────────────────────────

        [Fact]
        public async Task InvalidOtp_DoesNotIssueTheRealLoginCookie()
        {
            using var host = BuildServer();
            var client = host.Server.CreateClient();

            var registerResponse = await client.GetAsync("/simulate-register");
            var pendingCookie = ExtractCookieByName(registerResponse, PendingCookieName)!;

            var verifyResponse = await client.SendAsync(RequestWithCookie("/simulate-verify-otp?valid=false", pendingCookie));

            Assert.Null(ExtractCookieByName(verifyResponse, RealCookieName));
        }

        [Fact]
        public async Task InvalidOtp_DashboardRemainsInaccessible()
        {
            using var host = BuildServer();
            var client = host.Server.CreateClient();

            var registerResponse = await client.GetAsync("/simulate-register");
            var pendingCookie = ExtractCookieByName(registerResponse, PendingCookieName)!;
            await client.SendAsync(RequestWithCookie("/simulate-verify-otp?valid=false", pendingCookie));

            // Only the still-pending cookie exists — dashboard must still refuse it.
            var dashboardResponse = await client.SendAsync(RequestWithCookie("/dashboard", pendingCookie));
            Assert.Equal(HttpStatusCode.Unauthorized, dashboardResponse.StatusCode);
        }

        [Fact]
        public async Task NoPendingCookieAtAll_VerifyOtpIsRejected()
        {
            // A request with neither cookie at all (e.g. an expired pending cookie the
            // browser already dropped) — AuthenticateAsync fails, nothing is signed in.
            using var host = BuildServer();
            var client = host.Server.CreateClient();

            var verifyResponse = await client.GetAsync("/simulate-verify-otp?valid=true");

            Assert.Equal(HttpStatusCode.Unauthorized, verifyResponse.StatusCode);
            Assert.Null(ExtractCookieByName(verifyResponse, RealCookieName));
        }

        // ── Resend keeps the candidate reachable without ever granting full access ─

        [Fact]
        public async Task ResendOtp_KeepsThePendingCookieWorking_WithoutGrantingDashboardAccess()
        {
            using var host = BuildServer();
            var client = host.Server.CreateClient();

            var registerResponse = await client.GetAsync("/simulate-register");
            var pendingCookie = ExtractCookieByName(registerResponse, PendingCookieName)!;

            var resendResponse = await client.SendAsync(RequestWithCookie("/simulate-resend", pendingCookie));
            var refreshedPendingCookie = ExtractCookieByName(resendResponse, PendingCookieName) ?? pendingCookie;
            // Resend must not have granted the real cookie.
            Assert.Null(ExtractCookieByName(resendResponse, RealCookieName));

            var dashboardResponse = await client.SendAsync(RequestWithCookie("/dashboard", refreshedPendingCookie));
            Assert.Equal(HttpStatusCode.Unauthorized, dashboardResponse.StatusCode);

            // But the candidate can still reach the verification page and then verify.
            var verifyPageResponse = await client.SendAsync(RequestWithCookie("/verify-page", refreshedPendingCookie));
            Assert.Equal(HttpStatusCode.OK, verifyPageResponse.StatusCode);

            var verifyResponse = await client.SendAsync(RequestWithCookie("/simulate-verify-otp?valid=true", refreshedPendingCookie));
            Assert.NotNull(ExtractCookieByName(verifyResponse, RealCookieName));
        }
    }
}
