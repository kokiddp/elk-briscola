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
    IGameRepository games,
    IGameStateCodec stateCodec,
    IBriscolaEngine engine,
    IGameEventBus eventBus,
    IClock clock,
    ITimerService timers,
    IOptions<Configuration.GameOptions> options)
{
    private readonly ConcurrentDictionary<Guid, GameRoom> _rooms = new();

    public GameRoom GetOrCreate(GameRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);

        return _rooms.GetOrAdd(
            record.Id,
            _ => GameRoom.FromRecord(record, games, stateCodec, engine, eventBus, clock, timers, options));
    }

    public GameRoom GetOrCreate(Guid gameId, Func<GameRecord> lazyFactory)
    {
        ArgumentNullException.ThrowIfNull(lazyFactory);
        return _rooms.GetOrAdd(
            gameId,
            _ =>
            {
                GameRecord record = lazyFactory();
                return GameRoom.FromRecord(
                    record,
                    games,
                    stateCodec,
                    engine,
                    eventBus,
                    clock,
                    timers,
                    options);
            });
    }

    public async Task HydrateAsync(CancellationToken ct)
    {
        IReadOnlyList<GameRecord> records =
            await games.ListByStatusAsync(GameStatus.Running, take: int.MaxValue, ct).ConfigureAwait(false);

        foreach (GameRecord record in records)
        {
            _rooms.TryAdd(
                record.Id,
                GameRoom.FromRecord(record, games, stateCodec, engine, eventBus, clock, timers, options));
        }
    }

    public async Task EnqueueAsync(GameCommand command, CancellationToken ct = default)
    {
        GameRecord? record = await games.GetAsync(command.GameId, ct).ConfigureAwait(false);
        if (record is null)
        {
            throw new GameCommandException($"Game {command.GameId} was not found.");
        }

        GameRoom room = GetOrCreate(record);
        await room.EnqueueAsync(command, ct).ConfigureAwait(false);
    }
}
