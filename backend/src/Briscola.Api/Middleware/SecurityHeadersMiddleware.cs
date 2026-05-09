namespace Briscola.Api.Middleware;

/// <summary>
/// Writes a fixed set of security headers on every response. Hosted as
/// pipeline middleware (registered in <c>Program.cs</c> before routing).
/// HSTS is gated by <see cref="IHostEnvironment.IsProduction"/> because
/// dev runs over plain HTTP.
/// </summary>
public sealed class SecurityHeadersMiddleware
{
    private readonly RequestDelegate _next;
    private readonly bool _isProduction;

    public SecurityHeadersMiddleware(RequestDelegate next, IHostEnvironment env)
    {
        ArgumentNullException.ThrowIfNull(env);
        _next = next;
        _isProduction = env.IsProduction();
    }

    public Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        IHeaderDictionary headers = context.Response.Headers;
        headers.Append("X-Content-Type-Options", "nosniff");
        headers.Append("Referrer-Policy", "strict-origin-when-cross-origin");
        headers.Append("Permissions-Policy", "()");
        headers.Append("Content-Security-Policy",
            "default-src 'self'; img-src 'self' data:; connect-src 'self' wss:; "
            + "script-src 'self'; style-src 'self' 'unsafe-inline'");
        if (_isProduction)
        {
            headers.Append("Strict-Transport-Security",
                "max-age=31536000; includeSubDomains; preload");
        }

        return _next(context);
    }
}

public static class SecurityHeadersMiddlewareExtensions
{
    public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);
        return app.UseMiddleware<SecurityHeadersMiddleware>();
    }
}
