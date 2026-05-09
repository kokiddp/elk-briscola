using System.Collections.Immutable;
using Briscola.Application.Errors;
using Briscola.Application.Orchestration;
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
    GameOrchestrator orchestrator)
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
            StateSnapshotJson: string.Empty,
            BriscolaSuit: Suit.Bastoni,
            req.IsPrivate,
            hash,
            seatUserIds,
            Version: 0);

        await games.CreateAsync(record, ct).ConfigureAwait(false);
        return record;
    }

    public async Task<IReadOnlyList<GameSummary>> ListAsync(GameStatus status, CancellationToken ct)
    {
        IReadOnlyList<GameRecord> records =
            await games.ListByStatusAsync(status, take: 100, ct).ConfigureAwait(false);
        return records.Select(ToSummary).ToArray();
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
            ValidatePrivateGame(record, password);

            if (record.SeatUserIds.Contains(userId))
            {
                return record;
            }

            int seat = FirstFreeSeat(record);
            ImmutableArray<Guid?> seats = record.SeatUserIds.SetItem(seat, userId);
            GameRecord desired = record with { SeatUserIds = seats };

            if (seats.All(static id => id.HasValue))
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

            GameRecord desired = record with { SeatUserIds = record.SeatUserIds.SetItem(seat, null) };
            bool updated = await games.UpdateAsync(desired, ct).ConfigureAwait(false);
            if (updated)
            {
                return;
            }
        }

        throw new ConcurrencyConflictException($"Game {gameId} could not be left after retries.");
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

    private static GameSummary ToSummary(GameRecord record) =>
        new(
            record.Id,
            record.Mode,
            record.Name,
            record.Status,
            record.SeatUserIds.Count(static id => id.HasValue),
            record.SeatUserIds.Length,
            record.IsPrivate,
            record.CreatedAt,
            record.StartedAt);

    private static int PlayerCount(GameMode mode) => mode switch
    {
        GameMode.TwoPlayer => 2,
        GameMode.FourPlayerTeams => 4,
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown mode"),
    };
}
