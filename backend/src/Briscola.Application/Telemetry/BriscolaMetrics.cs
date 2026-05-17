using System.Diagnostics.Metrics;

namespace Briscola.Application.Telemetry;

/// <summary>
/// Custom OpenTelemetry instruments emitted by the engine + orchestration
/// layer. Registered as a singleton so the meter is built once at process
/// startup; the per-instrument primitives are thread-safe by contract.
///
/// The meter name <see cref="MeterName"/> is what OTel exporters key on,
/// so resist renaming after a dashboard ships against it.
/// </summary>
public sealed class BriscolaMetrics : IDisposable
{
    public const string MeterName = "Briscola";

    private readonly Meter _meter;
    private bool _disposed;

    public BriscolaMetrics()
    {
        _meter = new Meter(MeterName);
        ActiveGames = _meter.CreateUpDownCounter<int>(
            "briscola.active_games",
            unit: "{games}",
            description: "Games currently running in memory (post Orchestrator load, pre Finished).");
        ConnectedPlayers = _meter.CreateUpDownCounter<int>(
            "briscola.connected_players",
            unit: "{players}",
            description: "Player connections currently joined to a GameHub group.");
        MovesTotal = _meter.CreateCounter<long>(
            "briscola.moves_total",
            unit: "{moves}",
            description: "Cumulative PlayCard commands accepted by the engine.");
    }

    public UpDownCounter<int> ActiveGames { get; }
    public UpDownCounter<int> ConnectedPlayers { get; }
    public Counter<long> MovesTotal { get; }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _meter.Dispose();
    }
}
