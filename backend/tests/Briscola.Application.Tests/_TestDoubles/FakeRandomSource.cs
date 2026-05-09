using Briscola.Application.Ports;
using Briscola.Domain.Primitives;

namespace Briscola.Application.Tests.TestDoubles;

internal sealed class FakeRandomSource(long seed = 12345) : Ports.IRandomSource
{
    private readonly SeededRandomSource _inner = new(seed);

    public long Seed => _inner.Seed;

    public int Next(int maxExclusive) => _inner.Next(maxExclusive);
}
