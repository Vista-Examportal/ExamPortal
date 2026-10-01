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
