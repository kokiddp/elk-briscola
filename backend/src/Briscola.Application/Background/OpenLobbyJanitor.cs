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

    public async Task RunOnceAsync(CancellationToken ct)
    {
        await using IGameRepositoryScope scope = gamesFactory.Create();
        IGameRepository games = scope.Repository;
        IReadOnlyList<GameRecord> records =
            await games.ListByStatusAsync(GameStatus.Open, take: int.MaxValue, ct).ConfigureAwait(false);
        DateTimeOffset now = clock.UtcNow;
        DateTimeOffset cutoff = now.AddMinutes(-options.Value.OpenLobbyTtlMinutes);

        foreach (GameRecord record in records.Where(r => r.CreatedAt < cutoff))
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
