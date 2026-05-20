using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;

namespace Briscola.Api.RateLimiting;

public static class RateLimitingPolicies
{
    public const string AuthLogin = "auth-login";
    public const string AuthRegister = "auth-register";
    public const string AuthRefresh = "auth-refresh";
    public const string AuthChangePassword = "auth-change-password";
    public const string AuthLogout = "auth-logout";

    /// <summary>
    /// Default REST rate limits. Each policy is overridable via config under
    /// <c>RateLimits:&lt;Key&gt;:PermitLimit</c> / <c>WindowSeconds</c>
    /// (Section names mirror the policy keys above). The defaults match the
    /// README's documented limits; overriding to higher numbers is useful
    /// for soak tests and e2e harnesses without touching the production
    /// defaults.
    /// </summary>
    private static readonly Dictionary<string, (int PermitLimit, TimeSpan Window)> Defaults =
        new()
        {
            [AuthLogin] = (5, TimeSpan.FromMinutes(1)),
            [AuthRegister] = (3, TimeSpan.FromHours(1)),
            [AuthRefresh] = (30, TimeSpan.FromMinutes(1)),
            // change-password takes the *current* password and a new
            // one — a stolen access token can therefore be used to
            // brute-force the current password against an active session.
            // Cap by user, generously enough for legitimate retries.
            [AuthChangePassword] = (5, TimeSpan.FromMinutes(15)),
            // Logout is cheap server-side, but uncapped it can be used
            // to spray-revoke refresh tokens at full HTTP throughput.
            [AuthLogout] = (30, TimeSpan.FromMinutes(1)),
        };

    public static void Configure(RateLimiterOptions options)
        => Configure(options, configuration: null);

    public static void Configure(RateLimiterOptions options, IConfiguration? configuration)
    {
        ArgumentNullException.ThrowIfNull(options);

        options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

        (int permit, TimeSpan window) login = ReadLimits(configuration, AuthLogin);
        (int permit, TimeSpan window) register = ReadLimits(configuration, AuthRegister);
        (int permit, TimeSpan window) refresh = ReadLimits(configuration, AuthRefresh);
        (int permit, TimeSpan window) changePassword = ReadLimits(configuration, AuthChangePassword);
        (int permit, TimeSpan window) logout = ReadLimits(configuration, AuthLogout);

        options.AddPolicy(AuthLogin, ctx =>
            RateLimitPartition.GetFixedWindowLimiter(
                partitionKey: PartitionByIp(ctx),
                factory: _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = login.permit,
                    Window = login.window,
                    QueueLimit = 0,
                }));

        options.AddPolicy(AuthRegister, ctx =>
            RateLimitPartition.GetFixedWindowLimiter(
                partitionKey: PartitionByIp(ctx),
                factory: _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = register.permit,
                    Window = register.window,
                    QueueLimit = 0,
                }));

        options.AddPolicy(AuthRefresh, ctx =>
            RateLimitPartition.GetFixedWindowLimiter(
                partitionKey: PartitionByUser(ctx),
                factory: _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = refresh.permit,
                    Window = refresh.window,
                    QueueLimit = 0,
                }));

        options.AddPolicy(AuthChangePassword, ctx =>
            RateLimitPartition.GetFixedWindowLimiter(
                partitionKey: PartitionByUser(ctx),
                factory: _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = changePassword.permit,
                    Window = changePassword.window,
                    QueueLimit = 0,
                }));

        options.AddPolicy(AuthLogout, ctx =>
            RateLimitPartition.GetFixedWindowLimiter(
                partitionKey: PartitionByUser(ctx),
                factory: _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = logout.permit,
                    Window = logout.window,
                    QueueLimit = 0,
                }));
    }

    private static (int permit, TimeSpan window) ReadLimits(IConfiguration? config, string key)
    {
        var (defaultPermit, defaultWindow) = Defaults[key];
        if (config is null)
        {
            return (defaultPermit, defaultWindow);
        }
        IConfigurationSection section = config.GetSection($"RateLimits:{key}");
        int permit = section.GetValue("PermitLimit", defaultPermit);
        int windowSeconds = section.GetValue("WindowSeconds", (int)defaultWindow.TotalSeconds);
        return (permit, TimeSpan.FromSeconds(windowSeconds));
    }

    private static string PartitionByIp(HttpContext ctx) =>
        ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown";

    private static string PartitionByUser(HttpContext ctx) =>
        ctx.User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? ctx.Connection.RemoteIpAddress?.ToString()
            ?? "unknown";
}
