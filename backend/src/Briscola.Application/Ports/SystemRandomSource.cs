using Briscola.Domain.Primitives;

namespace Briscola.Application.Ports;

public sealed class SystemRandomSource : IRandomSource
{
    private readonly SeededRandomSource _inner;

    public SystemRandomSource()
        : this(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds())
    {
    }

    public SystemRandomSource(long seed)
    {
        _inner = new SeededRandomSource(seed);
    }

    public long Seed => _inner.Seed;

    public int Next(int maxExclusive) => _inner.Next(maxExclusive);
}
