using System.Net;
using System.Threading.Tasks;
using ExamPortal.Configuration;
using ExamPortal.Middleware;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace ExamPortal.Tests.Middleware
{
    public class IpAllowListMiddlewareTests
    {
        private static (IpAllowListMiddleware Middleware, DefaultHttpContext Context, System.Func<bool> WasNextCalled) Build(
            string remoteIp, string path = "/")
        {
            var nextCalled = false;
            RequestDelegate next = _ =>
            {
                nextCalled = true;
                return Task.CompletedTask;
            };

            var middleware = new IpAllowListMiddleware(next, NullLogger<IpAllowListMiddleware>.Instance);

            var context = new DefaultHttpContext();
            context.Connection.RemoteIpAddress = IPAddress.Parse(remoteIp);
            context.Request.Path = path;

            return (middleware, context, () => nextCalled);
        }

        private static IOptions<IpAccessControlOptions> Options(
            bool enabled, System.Collections.Generic.List<string>? allowedIps = null,
            System.Collections.Generic.List<string>? protectedPrefixes = null)
        {
            return Microsoft.Extensions.Options.Options.Create(new IpAccessControlOptions
            {
                Enabled = enabled,
                AllowedIPs = allowedIps ?? new(),
                ProtectedPathPrefixes = protectedPrefixes ?? new(),
            });
        }

        [Fact]
        public async Task Disabled_AlwaysCallsNext_RegardlessOfIp()
        {
            var (middleware, context, wasNextCalled) = Build("8.8.8.8");

            await middleware.InvokeAsync(context, Options(enabled: false));

            Assert.True(wasNextCalled());
        }

        [Fact]
        public async Task Enabled_ExactIpMatch_CallsNext()
        {
            var (middleware, context, wasNextCalled) = Build("203.0.113.10");

            await middleware.InvokeAsync(context, Options(enabled: true, allowedIps: new() { "203.0.113.10" }));

            Assert.True(wasNextCalled());
        }

        [Fact]
        public async Task Enabled_IpNotInList_Returns403AndDoesNotCallNext()
        {
            var (middleware, context, wasNextCalled) = Build("8.8.8.8");

            await middleware.InvokeAsync(context, Options(enabled: true, allowedIps: new() { "203.0.113.10" }));

            Assert.False(wasNextCalled());
            Assert.Equal(StatusCodes.Status403Forbidden, context.Response.StatusCode);
        }

        [Fact]
        public async Task Enabled_IpInsideCidrRange_CallsNext()
        {
            var (middleware, context, wasNextCalled) = Build("203.0.113.42");

            await middleware.InvokeAsync(context, Options(enabled: true, allowedIps: new() { "203.0.113.0/24" }));

            Assert.True(wasNextCalled());
        }

        [Fact]
        public async Task Enabled_IpOutsideCidrRange_Returns403()
        {
            var (middleware, context, wasNextCalled) = Build("203.0.114.1");

            await middleware.InvokeAsync(context, Options(enabled: true, allowedIps: new() { "203.0.113.0/24" }));

            Assert.False(wasNextCalled());
            Assert.Equal(StatusCodes.Status403Forbidden, context.Response.StatusCode);
        }

        [Fact]
        public async Task Enabled_LocalhostShorthand_MatchesLoopbackAddress()
        {
            var (middleware, context, wasNextCalled) = Build("127.0.0.1");

            await middleware.InvokeAsync(context, Options(enabled: true, allowedIps: new() { "localhost" }));

            Assert.True(wasNextCalled());
        }

        [Fact]
        public async Task Enabled_LocalhostShorthand_DoesNotMatchNonLoopbackAddress()
        {
            var (middleware, context, wasNextCalled) = Build("203.0.113.10");

            await middleware.InvokeAsync(context, Options(enabled: true, allowedIps: new() { "localhost" }));

            Assert.False(wasNextCalled());
        }

        [Fact]
        public async Task Enabled_PathOutsideProtectedPrefixes_CallsNextRegardlessOfIp()
        {
            var (middleware, context, wasNextCalled) = Build("8.8.8.8", path: "/Careers");

            await middleware.InvokeAsync(context, Options(
                enabled: true,
                allowedIps: new() { "203.0.113.10" },
                protectedPrefixes: new() { "/Admin" }));

            Assert.True(wasNextCalled());
        }

        [Fact]
        public async Task Enabled_PathInsideProtectedPrefixes_EnforcesAllowList()
        {
            var (middleware, context, wasNextCalled) = Build("8.8.8.8", path: "/Admin/Dashboard");

            await middleware.InvokeAsync(context, Options(
                enabled: true,
                allowedIps: new() { "203.0.113.10" },
                protectedPrefixes: new() { "/Admin" }));

            Assert.False(wasNextCalled());
            Assert.Equal(StatusCodes.Status403Forbidden, context.Response.StatusCode);
        }

        [Fact]
        public async Task Enabled_EmptyProtectedPrefixes_ProtectsEntireApp()
        {
            var (middleware, context, wasNextCalled) = Build("8.8.8.8", path: "/Careers");

            await middleware.InvokeAsync(context, Options(
                enabled: true,
                allowedIps: new() { "203.0.113.10" },
                protectedPrefixes: new()));

            Assert.False(wasNextCalled());
            Assert.Equal(StatusCodes.Status403Forbidden, context.Response.StatusCode);
        }
    }
}
