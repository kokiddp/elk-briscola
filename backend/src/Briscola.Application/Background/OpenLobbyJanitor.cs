using Briscola.Application.Configuration;
using Briscola.Application.Persistence;
using Briscola.Application.Ports;
using Briscola.Domain.Primitives;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Briscola.Application.Background;

public sealed class OpenLobbyJanitor(
    IGameRepository games,
    IClock clock,
    IOptions<GameOptions> options) : BackgroundService
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
        IReadOnlyList<GameRecord> records =
            await games.ListByStatusAsync(GameStatus.Open, take: int.MaxValue, ct).ConfigureAwait(false);
        DateTimeOffset now = clock.UtcNow;
        DateTimeOffset cutoff = now.AddMinutes(-options.Value.OpenLobbyTtlMinutes);

        foreach (GameRecord record in records.Where(r => r.CreatedAt < cutoff))
        {
            await games.UpdateAsync(record with { Status = GameStatus.Abandoned, EndedAt = now }, ct)
                .ConfigureAwait(false);
        }
    }
}
