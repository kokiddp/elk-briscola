using System.Collections.Concurrent;
using Briscola.Application.Orchestration.Commands;
using Briscola.Application.Orchestration.Timers;
using Briscola.Application.Persistence;
using Briscola.Application.Ports;
using Briscola.Domain.Engine;
using Briscola.Domain.Primitives;
using Microsoft.Extensions.Options;

namespace Briscola.Application.Orchestration;

public sealed class GameOrchestrator(
    IGameRepositoryFactory gamesFactory,
    IGameStateCodec stateCodec,
    IBriscolaEngine engine,
    IGameEventBus eventBus,
    IClock clock,
    ITimerService timers,
    IOptions<Configuration.GameOptions> options) : IAsyncDisposable
{
    private readonly ConcurrentDictionary<Guid, GameRoom> _rooms = new();

    public GameRoom GetOrCreate(GameRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);

        return GetOrCreateCore(record.Id, () => record);
    }

    public GameRoom GetOrCreate(Guid gameId, Func<GameRecord> lazyFactory)
    {
        ArgumentNullException.ThrowIfNull(lazyFactory);
        return GetOrCreateCore(gameId, lazyFactory);
    }

    public async Task HydrateAsync(CancellationToken ct)
    {
        IReadOnlyList<GameRecord> records;
        await using (IGameRepositoryScope scope = gamesFactory.Create())
        {
            records = await scope.Repository
                .ListByStatusAsync(GameStatus.Running, take: int.MaxValue, ct)
                .ConfigureAwait(false);
        }

        foreach (GameRecord record in records)
        {
            // Build the candidate eagerly so we can dispose the orphan
            // when TryAdd loses the race against a concurrent GetOrCreate
            // (Phase 2 follow-up: hydrate orphan task).
            GameRoom candidate = GameRoom.FromRecord(
                record, gamesFactory, stateCodec, engine, eventBus, clock, timers, options);
            if (!_rooms.TryAdd(record.Id, candidate))
            {
                await candidate.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    public async Task EnqueueAsync(GameCommand command, CancellationToken ct = default)
    {
        GameRecord? record;
        await using (IGameRepositoryScope scope = gamesFactory.Create())
        {
            record = await scope.Repository.GetAsync(command.GameId, ct).ConfigureAwait(false);
        }

        if (record is null)
        {
            throw new GameCommandException($"Game {command.GameId} was not found.");
        }

        GameRoom room = GetOrCreate(record);
        await room.EnqueueAsync(command, ct).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        foreach (GameRoom room in _rooms.Values)
        {
            await room.DisposeAsync().ConfigureAwait(false);
        }

        _rooms.Clear();
    }

    private GameRoom GetOrCreateCore(Guid gameId, Func<GameRecord> lazyFactory)
    {
        if (_rooms.TryGetValue(gameId, out GameRoom? existing))
        {
            return existing;
        }

        // Build the candidate outside GetOrAdd so we can dispose it on a
        // lost TryAdd race rather than leaking its ProcessLoopAsync task
        // and timers (Phase 2 follow-up: hydrate orphan task).
        GameRecord record = lazyFactory();
        GameRoom candidate = GameRoom.FromRecord(
            record, gamesFactory, stateCodec, engine, eventBus, clock, timers, options);
        if (_rooms.TryAdd(gameId, candidate))
        {
            return candidate;
        }

        // Lost the race. The other thread's instance is the canonical one.
        // Drain the orphan asynchronously; a background dispose is fine
        // because the orphan never received any commands.
        _ = DisposeOrphanAsync(candidate);
        return _rooms[gameId];
    }

    private static async Task DisposeOrphanAsync(GameRoom orphan)
    {
        await orphan.DisposeAsync().ConfigureAwait(false);
    }
}
