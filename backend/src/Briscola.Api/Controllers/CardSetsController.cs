using Briscola.Api.Configuration;
using Briscola.Api.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Briscola.Api.Controllers;

[ApiController]
[Route("api/v1/card-sets")]
[Produces("application/json")]
[AllowAnonymous]
public sealed class CardSetsController : ControllerBase
{
    private readonly CardSetCatalog _catalog;

    public CardSetsController(CardSetCatalog catalog) => _catalog = catalog;

    [HttpGet("")]
    [ProducesResponseType(typeof(IEnumerable<CardSetManifestDto>), StatusCodes.Status200OK)]
    public IActionResult Get() => Ok(_catalog.All);
}
