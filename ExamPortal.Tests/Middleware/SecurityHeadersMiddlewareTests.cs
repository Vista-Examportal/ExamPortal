using System.Linq;
using System.Threading.Tasks;
using ExamPortal.Middleware;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace ExamPortal.Tests.Middleware
{
    public class SecurityHeadersMiddlewareTests
    {
        // UseSecurityHeaders() is an app.Use(...) lambda middleware rather than a class
        // with its own InvokeAsync, so — unlike IpAllowListMiddleware — there's no way to
        // instantiate and call it directly. A minimal TestServer host is the standard way
        // to exercise middleware registered this way: spin up a tiny in-memory pipeline,
        // send one real HttpClient request through it, and inspect the response headers
        // that actually came back.
        private static async Task<System.Net.Http.HttpResponseMessage> SendThroughMiddlewareAsync()
        {
            using var host = await new HostBuilder()
                .ConfigureWebHost(webBuilder =>
                {
                    webBuilder.UseTestServer();
                    webBuilder.Configure(app =>
                    {
                        app.UseSecurityHeaders();
                        app.Run(async ctx => await ctx.Response.WriteAsync("ok"));
                    });
                })
                .StartAsync();

            var client = host.GetTestClient();
            return await client.GetAsync("/");
        }

        [Fact]
        public async Task SetsXContentTypeOptionsHeader()
        {
            var response = await SendThroughMiddlewareAsync();

            Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").First());
        }

        [Fact]
        public async Task SetsXFrameOptionsHeader_ToDeny()
        {
            var response = await SendThroughMiddlewareAsync();

            // DENY (not e.g. SAMEORIGIN) — clickjacking defense; this page should never
            // be framed by anyone, including this app's own other pages.
            Assert.Equal("DENY", response.Headers.GetValues("X-Frame-Options").First());
        }

        [Fact]
        public async Task SetsReferrerPolicyHeader()
        {
            var response = await SendThroughMiddlewareAsync();

            Assert.Equal("strict-origin-when-cross-origin", response.Headers.GetValues("Referrer-Policy").First());
        }

        [Fact]
        public async Task SetsContentSecurityPolicyHeader_WithDefaultSelfAndFrameAncestorsNone()
        {
            var response = await SendThroughMiddlewareAsync();
            var csp = response.Headers.GetValues("Content-Security-Policy").First();

            Assert.Contains("default-src 'self'", csp);
            Assert.Contains("frame-ancestors 'none'", csp);
        }

        [Fact]
        public async Task ContentSecurityPolicy_AllowsGoogleProfilePhotos()
        {
            // Candidates who sign in with Google get ProfilePhotoPath set to a
            // lh3.googleusercontent.com URL — if this host is ever dropped from img-src,
            // those photos silently stop rendering instead of throwing anything visible.
            var response = await SendThroughMiddlewareAsync();
            var csp = response.Headers.GetValues("Content-Security-Policy").First();

            Assert.Contains("lh3.googleusercontent.com", csp);
        }
    }
}
