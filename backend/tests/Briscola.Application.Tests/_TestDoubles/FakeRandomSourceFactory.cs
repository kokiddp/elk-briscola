using Briscola.Application.Ports;
using Briscola.Domain.Primitives;

namespace Briscola.Application.Tests.TestDoubles;

internal sealed class FakeRandomSourceFactory : IRandomSourceFactory
{
    private long _nextSeed = 12345;

    public IRandomSource Create() => new FakeRandomSource(_nextSeed++);
}
