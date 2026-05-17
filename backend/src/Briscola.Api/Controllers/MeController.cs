using System.Collections.Immutable;
using System.Text.Json;
using Briscola.Api.Dtos;
using Briscola.Application.History;
using Briscola.Application.Persistence;
using Briscola.Application.Ports;
using Briscola.Infrastructure.Persistence.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace Briscola.Api.Controllers;

/// <summary>
/// The authenticated user's own profile and ranking. All endpoints
/// require a valid bearer token — there's no public profile lookup.
/// </summary>
[ApiController]
[Route("api/v1/me")]
[Tags("Me")]
[Produces("application/json")]
[Authorize]
public sealed class MeController : ControllerBase
{
    private readonly UserManager<ApplicationUser> _users;
    private readonly IRankingRepository _rankings;
    private readonly MatchHistoryService _history;

    public MeController(
        UserManager<ApplicationUser> users,
        IRankingRepository rankings,
        MatchHistoryService history)
    {
        _users = users;
        _rankings = rankings;
        _history = history;
    }

    /// <summary>Get the authenticated user's profile + current ranking.</summary>
    /// <response code="200">Profile + ranking snapshot.</response>
    /// <response code="401">Bearer token missing or invalid.</response>
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

    /// <summary>Update mutable profile fields (display name, active card set).</summary>
    /// <remarks>
    /// Only the fields present in the body are updated. Unknown
    /// <c>activeCardSetId</c> values are accepted on the server but the
    /// frontend should validate against <c>GET /api/v1/card-sets</c>.
    /// </remarks>
    /// <response code="200">Profile after the patch.</response>
    /// <response code="400">Validation failed (e.g. display name too long).</response>
    [HttpPatch("")]
    [ProducesResponseType(typeof(MeResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
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

    /// <summary>Get just the ranking (Elo + W/L/D counters) for the authenticated user.</summary>
    /// <remarks>
    /// Lighter than <c>GET /me</c> when only ranking changes after a game.
    /// New accounts start at Elo 1500 (canonical seed).
    /// </remarks>
    /// <response code="200">Current ranking snapshot.</response>
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

    /// <summary>Paginated history of Finished games this user was seated at.</summary>
    /// <remarks>
    /// Newest game first. <paramref name="page"/> is 1-based;
    /// <paramref name="size"/> is clamped to <c>[1, 100]</c> with a default of
    /// <see cref="MatchHistoryService.DefaultPageSize"/>. Total count is
    /// returned so the UI can render a pager without a second request.
    /// </remarks>
    /// <response code="200">Page of match-history entries.</response>
    /// <response code="401">Bearer token missing or invalid.</response>
    [HttpGet("history")]
    [ProducesResponseType(typeof(MatchHistoryPageDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetHistory(
        [FromQuery] int page = 1,
        [FromQuery] int size = MatchHistoryService.DefaultPageSize,
        CancellationToken ct = default)
    {
        ApplicationUser? user = await _users.GetUserAsync(User).ConfigureAwait(false);
        if (user is null)
        {
            return Unauthorized();
        }

        PagedResult<MatchHistoryRow> result = await _history
            .ListForUserAsync(user.Id, page, size, ct)
            .ConfigureAwait(false);

        return Ok(new MatchHistoryPageDto(
            [.. result.Items.Select(ToDto)],
            result.Page,
            result.PageSize,
            result.TotalCount));
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

    private static MatchHistoryEntryDto ToDto(MatchHistoryRow row)
    {
        ImmutableArray<int> seatScores = DeserializeScores(row.SeatScoresJson);
        ImmutableArray<int>? teamScores = row.TeamScoresJson is null
            ? null
            : DeserializeScores(row.TeamScoresJson);

        return new MatchHistoryEntryDto(
            row.GameId,
            row.Mode,
            row.Name,
            row.StartedAt,
            row.EndedAt,
            row.MySeatIndex,
            row.SeatUserIds,
            row.OutcomeKind.ToString(),
            row.WinnerKey,
            seatScores,
            teamScores,
            row.Reason.ToString());
    }

    private static ImmutableArray<int> DeserializeScores(string json)
    {
        try
        {
            int[]? arr = JsonSerializer.Deserialize<int[]>(json);
            return arr is null ? ImmutableArray<int>.Empty : [.. arr];
        }
        catch (JsonException)
        {
            return ImmutableArray<int>.Empty;
        }
    }
}
