using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

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

    /// <summary>Readiness — service is ready to accept traffic. (Phase 11 will tighten this; for now it mirrors liveness.)</summary>
    [HttpGet("/readyz")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult Readiness() => Ok(new { status = "ready" });
}
