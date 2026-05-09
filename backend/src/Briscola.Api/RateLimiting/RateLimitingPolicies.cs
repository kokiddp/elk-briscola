using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace Briscola.Api.RateLimiting;

public static class RateLimitingPolicies
{
    public const string AuthLogin = "auth-login";
    public const string AuthRegister = "auth-register";
    public const string AuthRefresh = "auth-refresh";

    public static void Configure(RateLimiterOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

        options.AddPolicy(AuthLogin, ctx =>
            RateLimitPartition.GetFixedWindowLimiter(
                partitionKey: PartitionByIp(ctx),
                factory: _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 5,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0,
                }));

        options.AddPolicy(AuthRegister, ctx =>
            RateLimitPartition.GetFixedWindowLimiter(
                partitionKey: PartitionByIp(ctx),
                factory: _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 3,
                    Window = TimeSpan.FromHours(1),
                    QueueLimit = 0,
                }));

        options.AddPolicy(AuthRefresh, ctx =>
            RateLimitPartition.GetFixedWindowLimiter(
                partitionKey: PartitionByUser(ctx),
                factory: _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 30,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0,
                }));
    }

    private static string PartitionByIp(HttpContext ctx) =>
        ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown";

    private static string PartitionByUser(HttpContext ctx) =>
        ctx.User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? ctx.Connection.RemoteIpAddress?.ToString()
            ?? "unknown";
}
