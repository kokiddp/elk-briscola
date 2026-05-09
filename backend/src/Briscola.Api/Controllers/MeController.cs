using Briscola.Api.Dtos;
using Briscola.Application.Persistence;
using Briscola.Application.Ports;
using Briscola.Infrastructure.Persistence.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace Briscola.Api.Controllers;

[ApiController]
[Route("api/v1/me")]
[Produces("application/json")]
[Authorize]
public sealed class MeController : ControllerBase
{
    private readonly UserManager<ApplicationUser> _users;
    private readonly IRankingRepository _rankings;

    public MeController(UserManager<ApplicationUser> users, IRankingRepository rankings)
    {
        _users = users;
        _rankings = rankings;
    }

    [HttpGet("")]
    [ProducesResponseType(typeof(MeResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        ApplicationUser? user = await _users.GetUserAsync(User).ConfigureAwait(false);
        if (user is null)
        {
            return Unauthorized();
        }

        RankingRecord ranking = await _rankings.GetAsync(user.Id, ct).ConfigureAwait(false);
        return Ok(ToResponse(user, ranking));
    }

    [HttpPatch("")]
    [ProducesResponseType(typeof(MeResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> Patch([FromBody] MePatchRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        ApplicationUser? user = await _users.GetUserAsync(User).ConfigureAwait(false);
        if (user is null)
        {
            return Unauthorized();
        }

        bool changed = false;
        if (!string.IsNullOrEmpty(request.DisplayName) && request.DisplayName != user.DisplayName)
        {
            user.DisplayName = request.DisplayName;
            changed = true;
        }

        if (!string.IsNullOrEmpty(request.ActiveCardSetId) && request.ActiveCardSetId != user.ActiveCardSetId)
        {
            user.ActiveCardSetId = request.ActiveCardSetId;
            changed = true;
        }

        if (changed)
        {
            IdentityResult result = await _users.UpdateAsync(user).ConfigureAwait(false);
            if (!result.Succeeded)
            {
                return BadRequest(new
                {
                    code = "UpdateFailed",
                    errors = result.Errors.Select(e => new { e.Code, e.Description }),
                });
            }
        }

        RankingRecord ranking = await _rankings.GetAsync(user.Id, ct).ConfigureAwait(false);
        return Ok(ToResponse(user, ranking));
    }

    [HttpGet("ranking")]
    [ProducesResponseType(typeof(RankingDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetRanking(CancellationToken ct)
    {
        ApplicationUser? user = await _users.GetUserAsync(User).ConfigureAwait(false);
        if (user is null)
        {
            return Unauthorized();
        }

        RankingRecord ranking = await _rankings.GetAsync(user.Id, ct).ConfigureAwait(false);
        return Ok(ToDto(ranking));
    }

    private static MeResponse ToResponse(ApplicationUser user, RankingRecord ranking) =>
        new(
            user.Id,
            user.UserName ?? string.Empty,
            user.DisplayName,
            user.Email ?? string.Empty,
            user.ActiveCardSetId,
            ToDto(ranking));

    private static RankingDto ToDto(RankingRecord r) =>
        new(r.Elo, r.Wins, r.Losses, r.Draws, r.GamesPlayed, r.UpdatedAt);
}
