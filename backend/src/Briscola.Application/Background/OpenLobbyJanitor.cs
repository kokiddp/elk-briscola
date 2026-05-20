using System.Diagnostics.CodeAnalysis;
using Briscola.Application.Configuration;
using Briscola.Application.Orchestration.Events;
using Briscola.Application.Persistence;
using Briscola.Application.Ports;
using Briscola.Domain.Primitives;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Briscola.Application.Background;

public sealed class OpenLobbyJanitor(
    IGameRepositoryFactory gamesFactory,
    IClock clock,
    IOptions<GameOptions> options,
    IGameEventBus eventBus) : BackgroundService
{
    [ExcludeFromCodeCoverage]
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using PeriodicTimer timer = new(TimeSpan.FromMinutes(1));
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
        {
            await RunOnceAsync(stoppingToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Per-iteration cap on the number of expired games processed in a
    /// single tick. Bounds the worst-case fan-out of LobbyGameEnded
    /// publishes if a backlog accumulates (e.g. the janitor was idle
    /// for several windows). The next tick picks up any leftover.
    /// </summary>
    private const int MaxPerTick = 500;

    public async Task RunOnceAsync(CancellationToken ct)
    {
        await using IGameRepositoryScope scope = gamesFactory.Create();
        IGameRepository games = scope.Repository;
        DateTimeOffset now = clock.UtcNow;
        DateTimeOffset cutoff = now.AddMinutes(-options.Value.OpenLobbyTtlMinutes);
        IReadOnlyList<GameRecord> records =
            await games.ListExpiredOpenAsync(cutoff, MaxPerTick, ct).ConfigureAwait(false);

        foreach (GameRecord record in records)
        {
            GameRecord abandoned = record with { Status = GameStatus.Abandoned, EndedAt = now };
            bool updated = await games.UpdateAsync(abandoned, ct).ConfigureAwait(false);
            if (!updated)
            {
                continue;
            }

            // Tell the lobby SignalR group the game is gone so the SPA
            // can drop it from the open list + clear the creator's
            // pending banner. The dispatcher already wires LobbyGameEnded
            // → ILobbyClient.GameEnded(gameId).
            await eventBus.PublishAsync(new LobbyGameEndedEvent(record.Id, now), ct)
                .ConfigureAwait(false);
        }
    }
}
