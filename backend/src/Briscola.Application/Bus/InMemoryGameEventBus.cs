using System.Threading.Channels;
using Briscola.Application.Orchestration.Events;
using Briscola.Application.Ports;

namespace Briscola.Application.Bus;

public sealed class InMemoryGameEventBus : IGameEventBus
{
    private readonly Channel<IGameEvent> _channel =
        Channel.CreateUnbounded<IGameEvent>(
            new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });

    public ValueTask PublishAsync(IGameEvent evt, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(evt);
        return _channel.Writer.WriteAsync(evt, ct);
    }

    public IAsyncEnumerable<IGameEvent> ReadAllAsync(CancellationToken ct) =>
        _channel.Reader.ReadAllAsync(ct);
}
