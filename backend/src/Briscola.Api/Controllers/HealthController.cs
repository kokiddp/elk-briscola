using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Briscola.Api.Controllers;

[ApiController]
[AllowAnonymous]
public sealed class HealthController : ControllerBase
{
    [HttpGet("/healthz")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult Liveness() => Ok(new { status = "ok" });

    [HttpGet("/readyz")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult Readiness() => Ok(new { status = "ready" });
}
