namespace Briscola.Domain.Primitives;

public sealed class SeededRandomSource : IRandomSource
{
    private readonly Random _rng;

    public SeededRandomSource(long seed)
    {
        Seed = seed;
        // Random's int seed loses 32 bits of entropy; that's fine for a card
        // shuffle and keeps the seed printable as a long for replay logs.
        _rng = new Random(unchecked((int)seed));
    }

    public long Seed { get; }

    public int Next(int maxExclusive) => _rng.Next(maxExclusive);
}
