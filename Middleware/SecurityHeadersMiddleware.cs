namespace ExamPortal.Middleware
{
    /// <summary>
    /// Baseline security headers applied to every response — defends against
    /// clickjacking, MIME-type sniffing, and limits what cross-origin pages learn from
    /// the Referer header. Registered once in Program.cs via app.UseSecurityHeaders().
    /// </summary>
    public static class SecurityHeadersMiddleware
    {
        public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder app)
        {
            return app.Use(async (context, next) =>
            {
                context.Response.Headers["X-Content-Type-Options"] = "nosniff";
                context.Response.Headers["X-Frame-Options"] = "DENY";
                context.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
                // Camera + microphone (assessment proctoring, Exam/Take.cshtml getUserMedia) and
                // fullscreen (requestFullscreen) are allowed for this origin only. Everything
                // below is disabled because a code search confirms the app never uses it
                // (no geolocation/payment/USB/Bluetooth/Serial/MIDI/motion-sensor calls and no
                // getDisplayMedia screen capture). Re-enable here if a feature is ever added.
                context.Response.Headers["Permissions-Policy"] =
                    "camera=(self), microphone=(self), fullscreen=(self), " +
                    "geolocation=(), payment=(), usb=(), bluetooth=(), serial=(), midi=(), " +
                    "accelerometer=(), gyroscope=(), magnetometer=(), display-capture=()";
                context.Response.Headers["Content-Security-Policy"] =
                    "default-src 'self'; " +
                    "script-src 'self' https://cdn.jsdelivr.net https://unpkg.com https://cdnjs.cloudflare.com 'unsafe-inline'; " +
                    "style-src 'self' https://cdn.jsdelivr.net https://cdnjs.cloudflare.com https://fonts.googleapis.com 'unsafe-inline'; " +
                    "font-src 'self' https://cdnjs.cloudflare.com https://fonts.gstatic.com; " +
                    // 'self' + data: for locally-uploaded/generated images; lh3.googleusercontent.com
                    // specifically for candidate profile photos pulled from Google Sign-In
                    // (ProfilePhotoPath is set to Google's photo URL for those accounts —
                    // without this, the CSP would silently block the <img> from ever loading).
                    "img-src 'self' data: https://lh3.googleusercontent.com; " +
                    "frame-ancestors 'none'";
                await next();
            });
        }
    }
}
