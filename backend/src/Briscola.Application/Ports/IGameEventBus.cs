using Briscola.Application.Orchestration.Events;

namespace Briscola.Application.Ports;

public interface IGameEventBus
{
    ValueTask PublishAsync(IGameEvent evt, CancellationToken ct = default);
    IAsyncEnumerable<IGameEvent> ReadAllAsync(CancellationToken ct);
}
