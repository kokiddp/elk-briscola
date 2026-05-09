using Briscola.Application.Ports;

namespace Briscola.Application.Tests.TestDoubles;

internal sealed class FakeClock(DateTimeOffset initial) : IClock
{
    public DateTimeOffset UtcNow { get; private set; } = initial;

    public void Advance(TimeSpan by) => UtcNow = UtcNow.Add(by);
}
