using Briscola.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Briscola.Api.Controllers;

/// <summary>Liveness + readiness probes for orchestrators (Kubernetes, Docker healthcheck, etc.).</summary>
[ApiController]
[Tags("Health")]
[AllowAnonymous]
public sealed class HealthController : ControllerBase
{
    /// <summary>Liveness — process is up and the request pipeline is responsive.</summary>
    [HttpGet("/healthz")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult Liveness() => Ok(new { status = "ok" });

    /// <summary>
    /// Readiness — service is ready to accept traffic. Pings the database via
    /// <c>DatabaseFacade.CanConnectAsync</c>. Returns 503 on failure so
    /// orchestrators stop routing to a host whose DB just disappeared.
    /// </summary>
    [HttpGet("/readyz")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> Readiness(
        [FromServices] BriscolaDbContext db,
        CancellationToken ct)
    {
        bool ok;
        try
        {
            ok = await db.Database.CanConnectAsync(ct).ConfigureAwait(false);
        }
        catch
        {
            // Provider-specific exceptions (socket reset, auth fail, etc.)
            // all collapse into a 503 here — we don't want to leak driver
            // internals to a public probe.
            ok = false;
        }

        if (!ok)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                new { status = "db_unreachable" });
        }

        return Ok(new { status = "ready" });
    }
}
