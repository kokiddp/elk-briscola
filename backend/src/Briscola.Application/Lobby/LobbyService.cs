using System.Collections.Immutable;
using Briscola.Application.Errors;
using Briscola.Application.Orchestration;
using Briscola.Application.Orchestration.Events;
using Briscola.Application.Persistence;
using Briscola.Application.Ports;
using Briscola.Domain.Engine;
using Briscola.Domain.Primitives;
using Briscola.Domain.State;

namespace Briscola.Application.Lobby;

public sealed class LobbyService(
    IGameRepository games,
    IGamePasswordHasher passwordHasher,
    IGameStateCodec stateCodec,
    IBriscolaEngine engine,
    IRandomSourceFactory randomSourceFactory,
    IClock clock,
    GameOrchestrator orchestrator,
    IGameEventBus eventBus,
    IPlayerDirectory players)
{
    private const int MaxJoinRetries = 3;

    public async Task<GameRecord> CreateAsync(
        CreateGameRequest req,
        Guid creatorUserId,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(req);

        int seats = PlayerCount(req.Mode);
        ImmutableArray<Guid?> seatUserIds = Enumerable.Repeat<Guid?>(null, seats).ToImmutableArray();
        seatUserIds = seatUserIds.SetItem(0, creatorUserId);
        DateTimeOffset now = clock.UtcNow;
        string? hash = req.IsPrivate
            ? passwordHasher.Hash(req.Password ?? throw new InvalidPasswordException())
            : null;

        GameRecord record = new(
            Guid.NewGuid(),
            req.Mode,
            req.Name,
            GameStatus.Open,
            creatorUserId,
            now,
            StartedAt: null,
            EndedAt: null,
            ShuffleSeed: 0,
            // Postgres `jsonb` rejects empty strings; "{}" is a valid empty
            // object that's never read for Open games (GameRoom.FromRecord
            // is only called once a game transitions to Running, at which
            // point the orchestrator overwrites this with the serialized
            // GameState). The whitespace check in FromRecord remains the
            // safety net.
            StateSnapshotJson: "{}",
            BriscolaSuit: Suit.Bastoni,
            req.IsPrivate,
            hash,
            seatUserIds,
            Version: 0);

        await games.CreateAsync(record, ct).ConfigureAwait(false);
        GameSummary summary = await BuildSummaryAsync(record, ct).ConfigureAwait(false);
        await eventBus.PublishAsync(
            new LobbyGameCreatedEvent(record.Id, now, summary), ct).ConfigureAwait(false);
        return record;
    }

    public async Task<IReadOnlyList<GameSummary>> ListAsync(GameStatus status, CancellationToken ct)
    {
        IReadOnlyList<GameRecord> records =
            await games.ListByStatusAsync(status, take: 100, ct).ConfigureAwait(false);

        // One round-trip to resolve display names + current Elo for every
        // seated player across every visible game. The directory dedups
        // ids internally, so even with overlapping rosters we issue one
        // SELECT per ListAsync call.
        IEnumerable<Guid> allSeatedIds = records
            .SelectMany(r => r.SeatUserIds)
            .OfType<Guid>();
        IReadOnlyDictionary<Guid, PlayerInfo> directory =
            await players.GetAsync(allSeatedIds, ct).ConfigureAwait(false);

        return records.Select(r => ToSummary(r, directory)).ToArray();
    }

    public async Task<GameRecord> JoinAsync(
        Guid gameId,
        Guid userId,
        string? password,
        CancellationToken ct)
    {
        for (int attempt = 0; attempt < MaxJoinRetries; attempt++)
        {
            GameRecord record = await LoadOpenGameAsync(gameId, ct).ConfigureAwait(false);

            // Idempotency check FIRST. A caller who's already seated
            // shouldn't be told their password is wrong — they're
            // already in, the call's a no-op. The previous order
            // surfaced InvalidPassword to a re-presenting seated user
            // with the wrong (or missing) password in their retry. L10.
            if (record.SeatUserIds.Contains(userId))
            {
                return record;
            }

            ValidatePrivateGame(record, password);

            int seat = FirstFreeSeat(record);
            ImmutableArray<Guid?> seats = record.SeatUserIds.SetItem(seat, userId);
            GameRecord desired = record with { SeatUserIds = seats };

            bool transitioningToRunning = seats.All(static id => id.HasValue);
            if (transitioningToRunning)
            {
                desired = StartRecord(desired, seats);
            }

            bool updated = await games.UpdateAsync(desired, ct).ConfigureAwait(false);
            if (!updated)
            {
                continue;
            }

            GameRecord saved = desired with { Version = desired.Version + 1 };
            if (saved.Status == GameStatus.Running)
            {
                orchestrator.GetOrCreate(saved);
            }

            DateTimeOffset now = clock.UtcNow;
            GameSummary updatedSummary = await BuildSummaryAsync(saved, ct).ConfigureAwait(false);
            await eventBus.PublishAsync(
                new LobbyGameUpdatedEvent(saved.Id, now, updatedSummary), ct).ConfigureAwait(false);
            if (transitioningToRunning)
            {
                await eventBus.PublishAsync(
                    new LobbyGameStartedEvent(saved.Id, now), ct).ConfigureAwait(false);
            }

            return saved;
        }

        throw new ConcurrencyConflictException($"Game {gameId} could not be joined after retries.");
    }

    public async Task LeaveAsync(Guid gameId, Guid userId, CancellationToken ct)
    {
        for (int attempt = 0; attempt < MaxJoinRetries; attempt++)
        {
            GameRecord record = await games.GetAsync(gameId, ct).ConfigureAwait(false)
                ?? throw new GameNotFoundException(gameId);
            if (record.Status != GameStatus.Open)
            {
                throw new LobbyConflictException("Cannot leave a game after it has started.");
            }

            int seat = record.SeatUserIds.IndexOf(userId);
            if (seat < 0)
            {
                return;
            }

            ImmutableArray<Guid?> nextSeats = record.SeatUserIds.SetItem(seat, null);
            DateTimeOffset now = clock.UtcNow;
            // If the leaver was the last player at the table, the row would
            // sit around as an empty Open game until the janitor reaped it
            // on its next minute tick. That's a confusing window — the
            // lobby would show a 0/N seat row that nobody can usefully
            // join. Collapse the game straight to Abandoned + emit
            // GameEnded so the SPA drops it from the open list immediately.
            bool lastPlayerLeft = nextSeats.All(static id => !id.HasValue);
            GameRecord desired = lastPlayerLeft
                ? record with
                {
                    SeatUserIds = nextSeats,
                    Status = GameStatus.Abandoned,
                    EndedAt = now,
                }
                : record with { SeatUserIds = nextSeats };
            bool updated = await games.UpdateAsync(desired, ct).ConfigureAwait(false);
            if (updated)
            {
                GameRecord saved = desired with { Version = desired.Version + 1 };
                if (lastPlayerLeft)
                {
                    await eventBus.PublishAsync(
                        new LobbyGameEndedEvent(saved.Id, now), ct).ConfigureAwait(false);
                }
                else
                {
                    GameSummary updatedSummary = await BuildSummaryAsync(saved, ct).ConfigureAwait(false);
                    await eventBus.PublishAsync(
                        new LobbyGameUpdatedEvent(saved.Id, now, updatedSummary), ct).ConfigureAwait(false);
                }
                return;
            }
        }

        throw new ConcurrencyConflictException($"Game {gameId} could not be left after retries.");
    }

    /// <summary>
    /// Builds a single-game summary enriched with display names + Elo.
    /// One DB round-trip per call. Use the bulk path in
    /// <see cref="ListAsync"/> when summarising many games at once.
    /// </summary>
    private async Task<GameSummary> BuildSummaryAsync(GameRecord record, CancellationToken ct)
    {
        IEnumerable<Guid> seated = record.SeatUserIds.OfType<Guid>();
        IReadOnlyDictionary<Guid, PlayerInfo> directory =
            await players.GetAsync(seated, ct).ConfigureAwait(false);
        return ToSummary(record, directory);
    }

    private async Task<GameRecord> LoadOpenGameAsync(Guid gameId, CancellationToken ct)
    {
        GameRecord record = await games.GetAsync(gameId, ct).ConfigureAwait(false)
            ?? throw new GameNotFoundException(gameId);
        if (record.Status != GameStatus.Open)
        {
            throw new LobbyConflictException("Only open games can be joined.");
        }

        return record;
    }

    private void ValidatePrivateGame(GameRecord record, string? password)
    {
        if (!record.IsPrivate)
        {
            return;
        }

        if (password is null || record.PasswordHash is null || !passwordHasher.Verify(password, record.PasswordHash))
        {
            throw new InvalidPasswordException();
        }
    }

    private GameRecord StartRecord(GameRecord record, ImmutableArray<Guid?> seats)
    {
        ImmutableArray<Guid> players = seats.Select(id => id!.Value).ToImmutableArray();
        Briscola.Domain.Primitives.IRandomSource randomSource = randomSourceFactory.Create();

        // Pick a dealer at random (per the canonical Briscola rules in README).
        // The pick comes from the same seeded source that drives the shuffle so
        // games stay deterministically replayable from ShuffleSeed.
        int dealerSeat = randomSource.Next(players.Length);

        GameState state = engine.StartGame(
            new GameSetup(record.Id, record.Mode, dealerSeat, players),
            randomSource);
        DateTimeOffset now = clock.UtcNow;
        return record with
        {
            Status = GameStatus.Running,
            StartedAt = now,
            ShuffleSeed = state.ShuffleSeed,
            BriscolaSuit = state.BriscolaSuit,
            StateSnapshotJson = stateCodec.Serialize(state),
        };
    }

    private static int FirstFreeSeat(GameRecord record)
    {
        int seat = record.SeatUserIds.IndexOf(null);
        if (seat < 0)
        {
            throw new LobbyConflictException("Game is already full.");
        }

        return seat;
    }

    private static GameSummary ToSummary(
        GameRecord record,
        IReadOnlyDictionary<Guid, PlayerInfo> directory) =>
        new(
            record.Id,
            record.Mode,
            record.Name,
            record.Status,
            record.SeatUserIds.Count(static id => id.HasValue),
            record.SeatUserIds.Length,
            record.IsPrivate,
            record.CreatedAt,
            record.StartedAt,
            record.SeatUserIds
                .Select(id => id is { } uid && directory.TryGetValue(uid, out PlayerInfo? info)
                    ? info
                    : null)
                .ToImmutableArray());

    private static int PlayerCount(GameMode mode) => mode switch
    {
        GameMode.TwoPlayer => 2,
        GameMode.FourPlayerTeams => 4,
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown mode"),
    };
}
