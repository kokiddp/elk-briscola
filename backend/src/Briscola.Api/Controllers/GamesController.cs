using Briscola.Api.Dtos;
using Briscola.Application.Lobby;
using Briscola.Application.Persistence;
using Briscola.Application.Ports;
using Briscola.Domain.Primitives;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Briscola.Api.Controllers;

/// <summary>
/// Lobby-side game lifecycle: list, create, join, leave. Once a game
/// transitions to <c>Running</c> all real-time interactions move to the
/// SignalR <c>/hubs/game</c> channel — see <c>docs/asyncapi.json</c>
/// (or the AsyncAPI viewer at <c>/docs/asyncapi</c> in dev).
/// </summary>
[ApiController]
[Route("api/v1/games")]
[Tags("Games")]
[Produces("application/json")]
[Authorize]
public sealed class GamesController : ControllerBase
{
    private readonly LobbyService _lobby;
    private readonly IGameRepository _games;
    private readonly IUserContext _user;

    public GamesController(LobbyService lobby, IGameRepository games, IUserContext user)
    {
        _lobby = lobby;
        _games = games;
        _user = user;
    }

    /// <summary>List games filtered by <paramref name="status"/>.</summary>
    /// <param name="status">Open / Running / Finished. The lobby UI typically requests Open.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <response code="200">Up to 100 games matching <paramref name="status"/>, newest first.</response>
    [HttpGet("")]
    [ProducesResponseType(typeof(IEnumerable<GameSummaryDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List([FromQuery] GameStatus status, CancellationToken ct)
    {
        IReadOnlyList<GameSummary> summaries = await _lobby.ListAsync(status, ct).ConfigureAwait(false);
        return Ok(summaries.Select(ToSummary));
    }

    /// <summary>Create a new Open game; the caller takes seat 0.</summary>
    /// <remarks>
    /// The game stays Open until every seat is filled (2 for TwoPlayer,
    /// 4 for FourPlayerTeams). Filling the last seat transitions it to
    /// Running and the orchestrator publishes the initial state via
    /// SignalR. Pass <c>isPrivate=true</c> + a <c>password</c> to gate
    /// the join.
    /// </remarks>
    /// <response code="201">Created game; Location header points to GET /games/{id}.</response>
    /// <response code="400">Validation failed (e.g. private game without password).</response>
    [HttpPost("")]
    [ProducesResponseType(typeof(GameDetailDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Create([FromBody] CreateGameRequestDto request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Game names are no longer collected by the UI. Coalesce
        // null / empty to "" so the persistence layer's non-null
        // column stays satisfied without a migration.
        CreateGameRequest payload = new(
            request.Mode,
            request.Name ?? string.Empty,
            request.IsPrivate,
            request.Password);
        GameRecord record = await _lobby
            .CreateAsync(payload, _user.UserId, ct)
            .ConfigureAwait(false);
        GameDetailDto dto = ToDetail(record);
        return CreatedAtAction(nameof(Get), new { id = record.Id }, dto);
    }

    /// <summary>Fetch a game's lobby-level detail (status, seats, metadata).</summary>
    /// <remarks>
    /// REST returns the public envelope only; in-game state (hands, scores,
    /// current trick) is hub-side and never exposed via REST.
    /// </remarks>
    /// <response code="200">Game detail.</response>
    /// <response code="404">No such game.</response>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(GameDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        GameRecord? record = await _games.GetAsync(id, ct).ConfigureAwait(false);
        return record is null ? NotFound() : Ok(ToDetail(record));
    }

    /// <summary>Join an Open game.</summary>
    /// <remarks>
    /// Idempotent for the same caller — re-joining returns the current
    /// detail without changing seats. Filling the last seat transitions
    /// the game to Running. Private games require the matching password.
    /// </remarks>
    /// <response code="200">Joined; payload is the post-join detail. If the game just transitioned to Running, <c>status</c> reflects that.</response>
    /// <response code="400">Wrong password on a private game.</response>
    /// <response code="404">No such game (or no longer Open).</response>
    /// <response code="409">Game is full or already Running.</response>
    [HttpPost("{id:guid}/join")]
    [ProducesResponseType(typeof(GameDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Join(Guid id, [FromBody] JoinGameRequestDto request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        GameRecord record = await _lobby
            .JoinAsync(id, _user.UserId, request.Password, ct)
            .ConfigureAwait(false);
        return Ok(ToDetail(record));
    }

    /// <summary>Leave an Open game (vacates the seat). Use the SignalR hub for Running games.</summary>
    /// <remarks>
    /// REST <c>leave</c> is the lobby exit only. For a Running game, the
    /// equivalent path is <c>GameHub.LeaveGame</c> which enqueues a
    /// <c>DisconnectCommand</c> + starts the reconnect grace timer.
    /// </remarks>
    /// <response code="204">Seat vacated (or caller wasn't seated — idempotent).</response>
    /// <response code="404">No such game.</response>
    /// <response code="409">Game is no longer Open.</response>
    [HttpPost("{id:guid}/leave")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Leave(Guid id, CancellationToken ct)
    {
        await _lobby.LeaveAsync(id, _user.UserId, ct).ConfigureAwait(false);
        return NoContent();
    }

    private static GameSummaryDto ToSummary(GameSummary s) =>
        new(s.Id, s.Mode, s.Name, s.Status, s.OccupiedSeats, s.TotalSeats, s.IsPrivate, s.CreatedAt, s.StartedAt);

    private static GameDetailDto ToDetail(GameRecord record) =>
        new(
            record.Id,
            record.Mode,
            record.Name,
            record.Status,
            record.IsPrivate,
            record.CreatedAt,
            record.StartedAt,
            record.EndedAt,
            record.SeatUserIds);
}
