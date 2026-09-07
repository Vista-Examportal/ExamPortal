using System.Net;
using ExamPortal.Configuration;
using Microsoft.Extensions.Options;

namespace ExamPortal.Middleware
{
    /// <summary>
    /// Application-level IP allow-list. When enabled (Configuration/IpAccessControlOptions.cs,
    /// "IpAccessControl" section, disabled by default), any request whose client IP is not in
    /// AllowedIPs gets a 403 before it reaches routing, auth, or any controller — including
    /// database-backed pages. See IpAccessControlOptions for why this is a defense-in-depth
    /// layer at the app's front door, not a replacement for restricting the database server
    /// itself at the infrastructure level.
    ///
    /// Registered once in Program.cs via app.UseIpAllowList(), as early in the pipeline as
    /// possible (before static files, routing, auth) so a blocked IP does the least possible
    /// work before being rejected.
    /// </summary>
    public class IpAllowListMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<IpAllowListMiddleware> _logger;

        public IpAllowListMiddleware(RequestDelegate next, ILogger<IpAllowListMiddleware> logger)
        {
            _next = next;
            _logger = logger;
        }

        public async Task InvokeAsync(HttpContext context, IOptions<IpAccessControlOptions> options)
        {
            var config = options.Value;

            if (!config.Enabled)
            {
                await _next(context);
                return;
            }

            // Empty prefix list = protect the whole app. Otherwise only enforce on paths
            // starting with one of the configured prefixes (e.g. just "/Admin").
            var path = context.Request.Path.Value ?? "";
            var isProtected = config.ProtectedPathPrefixes.Count == 0
                || config.ProtectedPathPrefixes.Any(p => path.StartsWith(p, StringComparison.OrdinalIgnoreCase));

            if (!isProtected)
            {
                await _next(context);
                return;
            }

            var remoteIp = context.Connection.RemoteIpAddress;
            if (remoteIp == null || !IsAllowed(remoteIp, config.AllowedIPs))
            {
                _logger.LogWarning(
                    "Blocked request to {Path} from disallowed IP {RemoteIp}",
                    path, remoteIp?.ToString() ?? "unknown");

                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                await context.Response.WriteAsync("Forbidden: your IP address is not permitted to access this application.");
                return;
            }

            await _next(context);
        }

        private static bool IsAllowed(IPAddress remoteIp, List<string> allowedEntries)
        {
            // IPv4-mapped IPv6 addresses (::ffff:127.0.0.1) show up when Kestrel is
            // dual-stack bound — normalize so a plain "127.0.0.1" entry still matches.
            var normalizedRemote = remoteIp.IsIPv4MappedToIPv6 ? remoteIp.MapToIPv4() : remoteIp;

            foreach (var entry in allowedEntries)
            {
                var trimmed = entry.Trim();
                if (trimmed.Length == 0) continue;

                if (trimmed.Equals("localhost", StringComparison.OrdinalIgnoreCase))
                {
                    if (IPAddress.IsLoopback(normalizedRemote)) return true;
                    continue;
                }

                if (trimmed.Contains('/'))
                {
                    if (IsInCidrRange(normalizedRemote, trimmed)) return true;
                    continue;
                }

                if (IPAddress.TryParse(trimmed, out var exact))
                {
                    var normalizedExact = exact.IsIPv4MappedToIPv6 ? exact.MapToIPv4() : exact;
                    if (normalizedExact.Equals(normalizedRemote)) return true;
                }
            }

            return false;
        }

        private static bool IsInCidrRange(IPAddress address, string cidr)
        {
            var parts = cidr.Split('/', 2);
            if (parts.Length != 2) return false;
            if (!IPAddress.TryParse(parts[0].Trim(), out var baseAddress)) return false;
            if (!int.TryParse(parts[1].Trim(), out var prefixLength)) return false;

            var addressBytes = address.GetAddressBytes();
            var baseBytes = baseAddress.GetAddressBytes();
            if (addressBytes.Length != baseBytes.Length) return false; // mismatched IPv4/IPv6 families
            if (prefixLength < 0 || prefixLength > addressBytes.Length * 8) return false;

            var fullBytes = prefixLength / 8;
            var remainingBits = prefixLength % 8;

            for (var i = 0; i < fullBytes; i++)
            {
                if (addressBytes[i] != baseBytes[i]) return false;
            }

            if (remainingBits > 0)
            {
                var mask = (byte)(0xFF << (8 - remainingBits));
                if ((addressBytes[fullBytes] & mask) != (baseBytes[fullBytes] & mask)) return false;
            }

            return true;
        }
    }

    public static class IpAllowListMiddlewareExtensions
    {
        public static IApplicationBuilder UseIpAllowList(this IApplicationBuilder app)
        {
            return app.UseMiddleware<IpAllowListMiddleware>();
        }
    }
}
