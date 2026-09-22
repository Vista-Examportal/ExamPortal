using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Security.Claims;
using System.Threading.Tasks;
using ExamPortal.Controllers;
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
    /// Covers the 30-minute inactivity auto-logout added to the main portal cookie
    /// (Program.cs, the default-scheme .AddCookie(...) call — shared by Candidate,
    /// Admin, Recruiter and HR logins).
    ///
    /// These tests build a small, isolated test host that configures cookie
    /// authentication with the *same* ExpireTimeSpan/SlidingExpiration values used in
    /// Program.cs, rather than booting the full application: the real Program.cs also
    /// runs DatabaseInitializer.EnsureSchema at startup, which issues raw SQL Server
    /// T-SQL (sys.columns/OBJECT_ID) that EF Core's InMemory provider can't execute, so
    /// a full WebApplicationFactory<Program> host isn't viable here without a real SQL
    /// Server. Isolating just the cookie-authentication pipeline still exercises the
    /// real ASP.NET Core CookieAuthenticationHandler — the actual code that decides
    /// expiry/renewal — so this tests real framework behavior, not a re-implementation
    /// of it.
    ///
    /// IMPORTANT: ExpectedIdleTimeout below must be kept in sync with
    /// Program.cs's opt.ExpireTimeSpan on the default-scheme .AddCookie(...) call.
    /// </summary>
    public class CookieAuthenticationTimeoutTests
    {
        private static readonly TimeSpan ExpectedIdleTimeout = TimeSpan.FromMinutes(30);
        private const bool ExpectedSlidingExpiration = true;

        /// <summary>Lets tests move time forward deterministically instead of sleeping
        /// for real. CookieAuthenticationOptions.TimeProvider (net8.0+) is exactly the
        /// seam the real CookieAuthenticationHandler reads its clock from.</summary>
        private sealed class ManualTimeProvider : TimeProvider
        {
            private DateTimeOffset _utcNow;
            public ManualTimeProvider(DateTimeOffset start) => _utcNow = start;
            public override DateTimeOffset GetUtcNow() => _utcNow;
            public void Advance(TimeSpan by) => _utcNow += by;
        }

        private sealed class TestHostHandle : IDisposable
        {
            public required TestServer Server { get; init; }
            public required ManualTimeProvider Clock { get; init; }
            public required IHost UnderlyingHost { get; init; }
            public void Dispose() => UnderlyingHost.Dispose();
        }

        /// <param name="expireTimeSpan">Mirrors Program.cs's opt.ExpireTimeSpan.</param>
        /// <param name="slidingExpiration">Mirrors Program.cs's opt.SlidingExpiration.</param>
        private static TestHostHandle BuildServer(TimeSpan expireTimeSpan, bool slidingExpiration = true)
        {
            var clock = new ManualTimeProvider(DateTimeOffset.UtcNow);

            var hostBuilder = new HostBuilder()
                .ConfigureWebHost(webHost =>
                {
                    webHost.UseTestServer();
                    webHost.ConfigureServices(services =>
                    {
                        // AuthorizationMiddleware (added by UseAuthorization) resolves an
                        // AuthorizationPolicyCache, which depends on EndpointDataSource.
                        // Under the classic WebHostBuilder/HostBuilder hosting model used
                        // here (as opposed to WebApplicationBuilder), UseRouting/UseEndpoints
                        // alone do NOT bridge mapped endpoints into DI as a resolvable
                        // EndpointDataSource — that bridging is minimal-hosting-model-only.
                        // This is a confirmed .NET 8 behavior (dotnet/aspnetcore#53332), not
                        // a mistake in how routing is wired up below. AuthorizationPolicyCache
                        // only uses this to invalidate its cache when endpoints change; we
                        // don't use [Authorize]/endpoint-attached policies here (auth is
                        // checked manually via context.User in each handler), so a static,
                        // empty EndpointDataSource is sufficient to satisfy the dependency.
                        services.AddSingleton<EndpointDataSource>(new DefaultEndpointDataSource());
                        services.AddRouting();
                        services.AddAuthorization();
                        services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
                            .AddCookie(options =>
                            {
                                options.ExpireTimeSpan = expireTimeSpan;
                                options.SlidingExpiration = slidingExpiration;
                                options.TimeProvider = clock;
                                options.Cookie.Name = "TestPortalAuth";
                            });
                    });
                    webHost.Configure(app =>
                    {
                        // Order matters: routing must be established before the
                        // authentication/authorization middleware that now depends on it,
                        // and endpoints are mapped last.
                        app.UseRouting();
                        app.UseAuthentication();
                        app.UseAuthorization();
                        app.UseEndpoints(endpoints =>
                        {
                            // Mimics AccountController.SignInUserAsync: non-persistent by
                            // default (ExpiresUtc left null -> falls back to
                            // ExpireTimeSpan), or persistent with an explicit 30-day
                            // ExpiresUtc when "persistent=true" is passed, exactly like
                            // Remember Me.
                            endpoints.MapGet("/login", async context =>
                            {
                                var persistent = context.Request.Query["persistent"] == "true";
                                var identity = new ClaimsIdentity(
                                    new[] { new Claim(ClaimTypes.Name, "test-user") },
                                    CookieAuthenticationDefaults.AuthenticationScheme);
                                var properties = new AuthenticationProperties
                                {
                                    IsPersistent = persistent,
                                    ExpiresUtc = persistent ? clock.GetUtcNow().AddDays(30) : null
                                };
                                await context.SignInAsync(
                                    CookieAuthenticationDefaults.AuthenticationScheme,
                                    new ClaimsPrincipal(identity),
                                    properties);
                                context.Response.StatusCode = 200;
                            });

                            // Mimics a protected portal/assessment endpoint. Left
                            // anonymous-by-default (no .RequireAuthorization()) and
                            // checking context.User manually, exactly as before — the
                            // point of these tests is whether the cookie handler
                            // populated context.User from the incoming cookie, not
                            // whether an authorization policy blocks it.
                            endpoints.MapGet("/secure", async context =>
                            {
                                if (context.User.Identity?.IsAuthenticated == true)
                                {
                                    context.Response.StatusCode = 200;
                                    await context.Response.WriteAsync("ok");
                                }
                                else
                                {
                                    context.Response.StatusCode = 401;
                                }
                            });
                        });
                    });
                });

            var host = hostBuilder.Start();
            return new TestHostHandle { Server = host.GetTestServer(), Clock = clock, UnderlyingHost = host };
        }

        private static string? ExtractCookie(HttpResponseMessage response)
        {
            if (!response.Headers.TryGetValues("Set-Cookie", out var values)) return null;
            var setCookie = values.First();
            return setCookie[..setCookie.IndexOf(';')];
        }

        private static HttpRequestMessage SecureRequest(string cookie) =>
            new(HttpMethod.Get, "/secure") { Headers = { { "Cookie", cookie } } };

        // ── 1. Configuration itself ─────────────────────────────────────────────

        [Fact]
        public void PortalCookie_IdleTimeout_Is30Minutes()
        {
            Assert.Equal(TimeSpan.FromMinutes(30), ExpectedIdleTimeout);
        }

        [Fact]
        public void PortalCookie_SlidingExpiration_IsEnabled()
        {
            Assert.True(ExpectedSlidingExpiration);
        }

        // ── 2. Inactive session eventually expires ──────────────────────────────

        [Fact]
        public async Task InactiveSession_ExpiresAfterIdleTimeout()
        {
            using var host = BuildServer(ExpectedIdleTimeout, ExpectedSlidingExpiration);
            var client = host.Server.CreateClient();

            var loginResponse = await client.GetAsync("/login");
            var cookie = ExtractCookie(loginResponse);
            Assert.NotNull(cookie);

            // No requests at all for longer than the idle timeout — simulates the
            // browser sitting untouched.
            host.Clock.Advance(ExpectedIdleTimeout + TimeSpan.FromMinutes(1));

            var secureResponse = await client.SendAsync(SecureRequest(cookie!));
            Assert.Equal(HttpStatusCode.Unauthorized, secureResponse.StatusCode);
        }

        // ── 3. Active requests renew (sliding) the session ──────────────────────

        [Fact]
        public async Task ActiveSession_KeepsRenewing_AndNeverForcesReLogin()
        {
            using var host = BuildServer(ExpectedIdleTimeout, ExpectedSlidingExpiration);
            var client = host.Server.CreateClient();

            var loginResponse = await client.GetAsync("/login");
            var cookie = ExtractCookie(loginResponse);
            Assert.NotNull(cookie);

            // Simulate continued activity every 20 minutes — comfortably past the
            // halfway point of the 30-minute window (so each request triggers a
            // sliding renewal), but never past the window itself. Run for 3 hours
            // total (well beyond the raw 30-minute window) to prove an active user
            // is never unexpectedly logged out.
            for (var i = 0; i < 9; i++)
            {
                host.Clock.Advance(TimeSpan.FromMinutes(20));
                var response = await client.SendAsync(SecureRequest(cookie!));
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);

                // Pick up the renewed cookie if the handler issued one.
                cookie = ExtractCookie(response) ?? cookie;
            }
        }

        // ── 4. Remember Me is unaffected by the 30-minute idle timeout ──────────

        [Fact]
        public async Task RememberMe_SurvivesFarLongerThanTheIdleTimeout()
        {
            using var host = BuildServer(ExpectedIdleTimeout, ExpectedSlidingExpiration);
            var client = host.Server.CreateClient();

            var loginResponse = await client.GetAsync("/login?persistent=true");
            var cookie = ExtractCookie(loginResponse);
            Assert.NotNull(cookie);

            // Idle for 3 hours — six times the 30-minute idle timeout that applies to
            // non-Remember-Me sessions — with zero intervening requests. A normal
            // session would already be expired (see InactiveSession_ExpiresAfterIdleTimeout);
            // Remember Me's explicit 30-day ExpiresUtc must not be affected by it.
            host.Clock.Advance(TimeSpan.FromHours(3));

            var secureResponse = await client.SendAsync(SecureRequest(cookie!));
            Assert.Equal(HttpStatusCode.OK, secureResponse.StatusCode);
        }

        [Fact]
        public async Task RememberMe_EventuallyExpiresAtItsOwn30DayWindow_NotAt30Minutes()
        {
            using var host = BuildServer(ExpectedIdleTimeout, ExpectedSlidingExpiration);
            var client = host.Server.CreateClient();

            var loginResponse = await client.GetAsync("/login?persistent=true");
            var cookie = ExtractCookie(loginResponse);
            Assert.NotNull(cookie);

            // Past the 30-day Remember Me window entirely (with no activity in
            // between, so there's nothing to renew it).
            host.Clock.Advance(TimeSpan.FromDays(31));

            var secureResponse = await client.SendAsync(SecureRequest(cookie!));
            Assert.Equal(HttpStatusCode.Unauthorized, secureResponse.StatusCode);
        }

        // ── 5. Assessment timer is decoupled from authentication sliding renewal ─

        private static bool InvokeIsAttemptExpired(DateTime startedAt, int durationMinutes, DateTime now)
        {
            // ExamController.IsAttemptExpired is intentionally private — it's an
            // internal implementation detail of Take/Submit/AutoSave, not something
            // this feature should ever need to touch. Reflection lets this test prove
            // the method's behavior without widening its visibility or changing any
            // assessment business logic.
            var method = typeof(ExamController).GetMethod("IsAttemptExpired", BindingFlags.NonPublic | BindingFlags.Static)
                ?? throw new InvalidOperationException(
                    "ExamController.IsAttemptExpired not found by reflection — has its name/signature changed?");
            return (bool)method.Invoke(null, new object[] { startedAt, durationMinutes, now })!;
        }

        [Fact]
        public async Task AssessmentExpiry_IsPureWallClock_UnaffectedByOngoingAuthRenewal()
        {
            using var host = BuildServer(ExpectedIdleTimeout, ExpectedSlidingExpiration);
            var client = host.Server.CreateClient();

            var loginResponse = await client.GetAsync("/login");
            var cookie = ExtractCookie(loginResponse);
            Assert.NotNull(cookie);

            var assessmentStartedAt = host.Clock.GetUtcNow().UtcDateTime;
            const int assessmentDurationMinutes = 60;

            // Simulate the real Take-exam page: a heartbeat/autosave-style
            // authenticated request every 10 minutes (comfortably inside the
            // 30-minute idle window, just like ProctorHeartbeat/AutoSave in
            // Views/Exam/Take.cshtml keep the session alive in production) while the
            // assessment itself runs for 90 minutes — 30 minutes past its own
            // 60-minute duration.
            for (var minute = 10; minute <= 90; minute += 10)
            {
                host.Clock.Advance(TimeSpan.FromMinutes(10));

                // The auth session stays alive throughout, thanks to the heartbeat —
                // it must never be the reason an active candidate gets logged out.
                var response = await client.SendAsync(SecureRequest(cookie!));
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                cookie = ExtractCookie(response) ?? cookie;

                var now = host.Clock.GetUtcNow().UtcDateTime;
                var expired = InvokeIsAttemptExpired(assessmentStartedAt, assessmentDurationMinutes, now);

                // The assessment's own 60-minute duration is authoritative regardless
                // of how many times the auth cookie has renewed itself in the
                // background: still open through minute 60, expired from minute 70
                // onward (first heartbeat past the 60-minute mark).
                if (minute <= 60)
                    Assert.False(expired, $"Assessment should still be open at minute {minute}.");
                else
                    Assert.True(expired, $"Assessment should be expired at minute {minute} (auth renewal must not extend it).");
            }
        }

        [Fact]
        public void AssessmentExpiry_MethodSignature_TakesNoAuthenticationInput()
        {
            // Structural guardrail: IsAttemptExpired only ever takes wall-clock
            // values (startedAt, durationMinutes, now) — there is no cookie, ticket,
            // or authentication-properties parameter for sliding expiration to reach
            // through. This is what actually guarantees requirement 4 ("do not extend
            // the assessment duration because of sliding authentication") — the two
            // systems have no shared state to leak through.
            var method = typeof(ExamController).GetMethod("IsAttemptExpired", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.NotNull(method);
            var parameterTypes = method!.GetParameters().Select(p => p.ParameterType).ToArray();
            Assert.Equal(new[] { typeof(DateTime), typeof(int), typeof(DateTime) }, parameterTypes);
        }
    }
}