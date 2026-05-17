using Serilog.Context;

namespace Briscola.Api.Middleware;

/// <summary>
/// Reads (or generates) an <c>X-Correlation-Id</c> per request, mirrors it
/// onto the response header, sets <c>HttpContext.TraceIdentifier</c>, and
/// pushes a <c>CorrelationId</c> property into Serilog's
/// <see cref="LogContext"/> so every log line emitted during the request —
/// including <c>UseSerilogRequestLogging</c>'s summary line — carries it.
///
/// The frontend's correlation-id interceptor sets the header on every
/// request; this middleware is the server-side half of the contract.
/// </summary>
public sealed class CorrelationIdMiddleware
{
    public const string HeaderName = "X-Correlation-Id";

    private readonly RequestDelegate _next;

    public CorrelationIdMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        string correlationId = ResolveCorrelationId(context);
        context.Response.Headers[HeaderName] = correlationId;
        context.TraceIdentifier = correlationId;

        using (LogContext.PushProperty("CorrelationId", correlationId))
        {
            await _next(context).ConfigureAwait(false);
        }
    }

    private static string ResolveCorrelationId(HttpContext context)
    {
        if (context.Request.Headers.TryGetValue(HeaderName, out var incoming))
        {
            string? value = incoming.ToString();
            if (!string.IsNullOrWhiteSpace(value) && value.Length <= 128)
            {
                return value;
            }
        }

        return Guid.NewGuid().ToString("N");
    }
}
