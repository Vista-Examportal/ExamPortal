using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Authentication.OAuth;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ExamPortal.Configuration;
using ExamPortal.Data;
using ExamPortal.Middleware;
using ExamPortal.Models;
using ExamPortal.Repositories;
using ExamPortal.Services;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllersWithViews(options =>
{
    options.Filters.Add(new AutoValidateAntiforgeryTokenAttribute());
})
    // EducationLevelEntry's string fields (DegreeOrCourse, InstituteName, BoardOrUniversity,
    // StreamBranch, etc.) are intentionally left without [Required] because their required-ness
    // is conditional and enforced in CandidateProfileStepViewModel.Validate(). With
    // <Nullable>enable</Nullable> in the csproj, MVC otherwise treats every non-nullable
    // reference-type property as implicitly required, which was overriding that conditional
    // logic and forcing all four fields on every level (including the 10th/Secondary level,
    // where they shouldn't be mandatory).
    .AddMvcOptions(options => options.SuppressImplicitRequiredAttributeForNonNullableReferenceTypes = true);
builder.Services.AddDbContext<AppDbContext>(opt =>
    opt.UseSqlServer(
        builder.Configuration.GetConnectionString("DefaultConnection"),
        sql => sql.EnableRetryOnFailure()));
builder.Services.AddMemoryCache();
builder.Services.Configure<EmailOptions>(builder.Configuration.GetSection(EmailOptions.SectionName));
// Reserved for future use — see Configuration/AzureOptions.cs and Configuration/JwtOptions.cs.
// Registering these now means any service that later needs them can take an
// IOptions<AzureOptions> / IOptions<JwtOptions> dependency with zero Program.cs changes.
builder.Services.Configure<AzureOptions>(builder.Configuration.GetSection(AzureOptions.SectionName));
builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection(JwtOptions.SectionName));
builder.Services.Configure<IpAccessControlOptions>(builder.Configuration.GetSection(IpAccessControlOptions.SectionName));
builder.Services.AddHttpClient("NotificationProvider", client =>
{
    client.Timeout = TimeSpan.FromSeconds(15);
});
builder.Services.AddHealthChecks()
    .AddCheck<DatabaseHealthCheck>("database");
builder.Services.AddScoped(typeof(IReadRepository<>), typeof(EfReadRepository<>));
builder.Services.AddScoped<FileStorageService>();
builder.Services.AddScoped<EligibilityService>();
builder.Services.AddScoped<NotificationService>();
builder.Services.AddScoped<AuditService>();
builder.Services.AddScoped<AssessmentScoringService>();
builder.Services.AddScoped<OfferLetterPdfService>();
builder.Services.AddScoped<EnterprisePortalService>();
builder.Services.AddScoped<ActivityFeedService>();
builder.Services.AddScoped<CandidateIdService>();
builder.Services.AddScoped<CandidateWorkflowService>();
builder.Services.AddScoped<CandidateRegistrationService>();
builder.Services.AddScoped<DisposableEmailService>();
builder.Services.AddScoped<AssessmentInvitationService>();
builder.Services.AddHostedService<NotificationBackgroundService>();
// ── Google Sign-In (Candidates only — see Controllers/AccountController.ExternalAuth.cs) ──
// Credentials are bound into GoogleAuthOptions (Configuration/GoogleAuthOptions.cs) from
// standard ASP.NET Core configuration, so they can come from (in override order)
// appsettings.json -> appsettings.{Environment}.json -> User Secrets (recommended for
// local dev: `dotnet user-secrets set "Authentication:Google:ClientId" "..."`) ->
// environment variables (Authentication__Google__ClientId /
// Authentication__Google__ClientSecret, recommended for production) -> Azure Key Vault,
// when configured. Never hardcoded. If not configured, Google sign-in is simply inactive
// (GoogleLogin shows a friendly error) rather than the app failing to start.
//
// Bound directly off IConfiguration here (rather than via DI/IOptions) because the value
// is needed immediately below to conditionally register the AddGoogle() handler itself —
// that decision has to be made while the service collection is still being built, before
// the container exists to inject into anything. builder.Services.Configure<>() further
// down still registers it for IOptions<GoogleAuthOptions> injection everywhere else.
var googleAuthOptions = builder.Configuration.GetSection(GoogleAuthOptions.SectionName).Get<GoogleAuthOptions>()
    ?? new GoogleAuthOptions();
builder.Services.Configure<GoogleAuthOptions>(builder.Configuration.GetSection(GoogleAuthOptions.SectionName));
var googleSignInConfigured = googleAuthOptions.IsConfigured;
builder.Services.AddSingleton(new GoogleSignInStatus(googleSignInConfigured));

var authBuilder = builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(opt => {
        opt.LoginPath = "/Account/Login";
        opt.AccessDeniedPath = "/Account/Login";
        // 30-minute inactivity auto-logout for Candidate/Admin/Recruiter/HR portal
        // sessions. SlidingExpiration renews this window on every authenticated
        // request, so an actively-used session is never logged out mid-use — only
        // genuine inactivity (no authenticated request for 30 minutes) expires the
        // ticket. This governs the *default* (non-Remember-Me) case only: it's the
        // fallback used when AuthenticationProperties.ExpiresUtc is left null (see
        // SignInUserAsync below) — a real 30-minute value now, instead of null just
        // meaning "whatever ExpireTimeSpan happens to be" as before.
        //
        // Remember Me is unaffected: SignInUserAsync sets an explicit ExpiresUtc
        // (+30 days) and IsPersistent = true when rememberMe is true, and the cookie
        // handler's sliding renewal re-issues each ticket using its own
        // (ExpiresUtc - IssuedUtc) span rather than this global ExpireTimeSpan — so a
        // Remember Me session keeps renewing itself in ~30-day increments, it never
        // collapses to 30 minutes.
        //
        // This is also independent of the assessment's own timer: ExamController's
        // IsAttemptExpired() is driven purely by Exam.DurationMinutes and
        // AssessmentAttempt.StartedAt (wall-clock), never by anything on this
        // cookie/ticket, so sliding this auth window can't extend — or shrink — an
        // assessment's configured end time. In practice a candidate actively on the
        // Take Exam page won't hit this timeout anyway: the existing proctoring
        // heartbeat (ProctorHeartbeat every 15s, AutoSave every 30s — see
        // Views/Exam/Take.cshtml) already fires authenticated requests well inside
        // this 30-minute window, renewing it on its own.
        opt.ExpireTimeSpan = TimeSpan.FromMinutes(30);
        opt.SlidingExpiration = true;
        opt.Cookie.HttpOnly = true;
        opt.Cookie.SameSite = SameSiteMode.Lax;
        opt.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
            ? CookieSecurePolicy.SameAsRequest
            : CookieSecurePolicy.Always;
    })
    // Short-lived, separate cookie the Google handler signs into — never the app's
    // real login. AccountController.ExternalAuth.cs.GoogleCallback reads it once,
    // performs candidate-only account lookup/creation, signs into the scheme above via
    // the existing SignInUserAsync, then clears this one.
    .AddCookie(AuthSchemes.GoogleExternal, opt =>
    {
        opt.Cookie.Name = "VWT.ExternalGoogle";
        opt.ExpireTimeSpan = TimeSpan.FromMinutes(10);
        opt.Cookie.HttpOnly = true;
        opt.Cookie.SameSite = SameSiteMode.Lax;
        opt.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
            ? CookieSecurePolicy.SameAsRequest
            : CookieSecurePolicy.Always;
    });

if (googleSignInConfigured)
{
    authBuilder.AddGoogle(GoogleDefaults.AuthenticationScheme, options =>
    {
        options.ClientId = googleAuthOptions.ClientId;
        options.ClientSecret = googleAuthOptions.ClientSecret;
        // Signs into the short-lived external cookie above, not the app's main
        // cookie — OAuth state/CSRF ("correlation cookie") protection is built into
        // this handler and applies before ExternalAuth.cs ever sees the callback.
        options.SignInScheme = AuthSchemes.GoogleExternal;
        options.CallbackPath = "/signin-google";
        options.SaveTokens = false;
        // Pulling picture/email_verified straight off Google's userinfo JSON via
        // OnCreatingTicket rather than ClaimActions.MapJsonKey — this event is part of
        // the base OAuth handler (OAuthEvents/OAuthCreatingTicketContext) and is a more
        // stable API surface across package versions than the ClaimActions extension
        // methods, which turned out to be version-sensitive to resolve.
        options.Events = new OAuthEvents
        {
            OnCreatingTicket = context =>
            {
                if (context.Identity == null) return Task.CompletedTask;
                if (context.User.TryGetProperty("picture", out var pictureProp) && pictureProp.ValueKind == System.Text.Json.JsonValueKind.String)
                    context.Identity.AddClaim(new System.Security.Claims.Claim("urn:google:picture", pictureProp.GetString() ?? ""));
                if (context.User.TryGetProperty("email_verified", out var verifiedProp))
                {
                    var verifiedText = verifiedProp.ValueKind == System.Text.Json.JsonValueKind.True ? "true"
                        : verifiedProp.ValueKind == System.Text.Json.JsonValueKind.False ? "false"
                        : verifiedProp.ToString();
                    context.Identity.AddClaim(new System.Security.Claims.Claim("urn:google:email_verified", verifiedText));
                }
                return Task.CompletedTask;
            },
            // Without this, a denied/cancelled Google consent screen (or any other
            // remote OAuth error) makes RemoteAuthenticationHandler throw an unhandled
            // AuthenticationFailureException instead of reaching GoogleCallback's own
            // remoteError handling below. This redirects there instead, passing the
            // failure message along as ?remoteError=..., so the user sees the friendly
            // "Google sign-in was cancelled or didn't complete" message and lands back
            // on Login rather than an error page.
            OnRemoteFailure = context =>
            {
                context.HandleResponse();
                var message = context.Failure?.Message ?? "access_denied";
                var redirectUrl = "/Account/GoogleCallback?remoteError=" + Uri.EscapeDataString(message);
                context.Response.Redirect(redirectUrl);
                return Task.CompletedTask;
            }
        };
    });
}

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("AuthSensitive", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                Window = TimeSpan.FromMinutes(1),
                PermitLimit = 10,
                QueueLimit = 0
            }));

    // Tighter than AuthSensitive: the Contact Us form is reachable by anyone with no
    // account and no CAPTCHA (see the auth-gap audit earlier in this project), so it's
    // the easiest target on the site for a spam bot. 5/hour per IP is enough for a
    // genuine visitor, not enough to be useful for abuse.
    options.AddPolicy("Contact", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                Window = TimeSpan.FromHours(1),
                PermitLimit = 5,
                QueueLimit = 0
            }));
});

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    // Demo-account password is sourced from config (.NET User Secrets in Development,
    // or the DemoAccounts__Password env var) — never hardcoded, never read outside
    // Development. See Data/AppDbContext.cs (DbSeeder.RequireDemoPassword).
    var demoAccountsPassword = app.Environment.IsDevelopment()
        ? app.Configuration["DemoAccounts:Password"]
        : null;
    DatabaseInitializer.EnsureSchema(db, seedDemoAccounts: app.Environment.IsDevelopment(), demoAccountsPassword: demoAccountsPassword);
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Site/Error");
    app.UseHsts();
}

// Catches 404s (mistyped/dead links), 403s from [Authorize], etc. and re-executes
// the pipeline against SiteController.StatusCodePage so visitors get a branded page
// instead of a bare, unstyled response — in every environment, not just Production,
// since a 404 isn't a sensitive failure the way an unhandled exception is.
app.UseStatusCodePagesWithReExecute("/Site/StatusCode/{0}");

// Ensure private upload directories exist. New candidate files are served through
// SecureFilesController rather than from public wwwroot static files.
using (var scope = app.Services.CreateScope())
{
    scope.ServiceProvider.GetRequiredService<FileStorageService>().EnsureDirectoriesExist();
}

// Application-level IP allow-list — disabled by default via "IpAccessControl:Enabled"
// in appsettings. When enabled, rejects requests from disallowed IPs before any other
// middleware runs (static files, routing, auth), so blocked traffic never reaches the
// database. See Middleware/IpAllowListMiddleware.cs and Configuration/IpAccessControlOptions.cs.
app.UseIpAllowList();

// Baseline security headers on every response — defends against clickjacking,
// MIME-type sniffing, and limits what cross-origin pages learn from the Referer header.
// See Middleware/SecurityHeadersMiddleware.cs.
app.UseSecurityHeaders();

if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}
app.UseStaticFiles();
app.UseRouting();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.MapHealthChecks("/health");
app.MapControllerRoute(name: "default", pattern: "{controller=Site}/{action=Index}/{id?}");
app.Run();