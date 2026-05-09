using Briscola.Api.Dtos;
using Briscola.Application.Lobby;
using Briscola.Application.Persistence;
using Briscola.Application.Ports;
using Briscola.Domain.Primitives;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Briscola.Api.Controllers;

[ApiController]
[Route("api/v1/games")]
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

    [HttpGet("")]
    [ProducesResponseType(typeof(IEnumerable<GameSummaryDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List([FromQuery] GameStatus status, CancellationToken ct)
    {
        IReadOnlyList<GameSummary> summaries = await _lobby.ListAsync(status, ct).ConfigureAwait(false);
        return Ok(summaries.Select(ToSummary));
    }

    [HttpPost("")]
    [ProducesResponseType(typeof(GameDetailDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Create([FromBody] CreateGameRequestDto request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        CreateGameRequest payload = new(request.Mode, request.Name, request.IsPrivate, request.Password);
        GameRecord record = await _lobby
            .CreateAsync(payload, _user.UserId, ct)
            .ConfigureAwait(false);
        GameDetailDto dto = ToDetail(record);
        return CreatedAtAction(nameof(Get), new { id = record.Id }, dto);
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(GameDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        GameRecord? record = await _games.GetAsync(id, ct).ConfigureAwait(false);
        return record is null ? NotFound() : Ok(ToDetail(record));
    }

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
