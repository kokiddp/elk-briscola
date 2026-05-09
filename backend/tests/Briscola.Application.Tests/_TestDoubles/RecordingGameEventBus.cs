using System.Threading.Channels;
using Briscola.Application.Orchestration.Events;
using Briscola.Application.Ports;

namespace Briscola.Application.Tests.TestDoubles;

internal sealed class RecordingGameEventBus : IGameEventBus
{
    private readonly Channel<IGameEvent> _channel = Channel.CreateUnbounded<IGameEvent>();
    private readonly List<IGameEvent> _events = [];

    public IReadOnlyList<IGameEvent> Events => _events;

    public ValueTask PublishAsync(IGameEvent evt, CancellationToken ct = default)
    {
        _events.Add(evt);
        return _channel.Writer.WriteAsync(evt, ct);
    }

    public IAsyncEnumerable<IGameEvent> ReadAllAsync(CancellationToken ct) =>
        _channel.Reader.ReadAllAsync(ct);
}
