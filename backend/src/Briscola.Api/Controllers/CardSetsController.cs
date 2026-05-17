using Briscola.Api.CardSets;
using Briscola.Api.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Briscola.Api.Controllers;

/// <summary>
/// Bundled card-set catalog. The list is loaded once at startup from
/// <c>wwwroot/card-sets/*</c>; adding a new set is a content-only change
/// (drop a manifest + image folder, rebuild). Anonymous because the
/// frontend needs the manifest before login to render the auth pages.
/// The cached payload is identical for every caller, so we hint at a
/// 5-minute response cache for any production proxy fronting the API.
/// </summary>
[ApiController]
[Route("api/v1/card-sets")]
[Tags("CardSets")]
[Produces("application/json")]
[AllowAnonymous]
[ResponseCache(Duration = 300, Location = ResponseCacheLocation.Any, NoStore = false)]
public sealed class CardSetsController : ControllerBase
{
    private readonly CardSetCatalog _catalog;

    public CardSetsController(CardSetCatalog catalog) => _catalog = catalog;

    /// <summary>List every bundled card set with its manifest.</summary>
    /// <response code="200">All registered card sets.</response>
    [HttpGet("")]
    [ProducesResponseType(typeof(IEnumerable<CardSetManifestDto>), StatusCodes.Status200OK)]
    public IActionResult Get() => Ok(_catalog.All);
}
