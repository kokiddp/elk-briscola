namespace Briscola.Domain.Primitives;

/// <summary>
/// Seedable random source used by the engine. Persisting <see cref="Seed"/>
/// alongside a game's move log makes the entire game replayable.
/// </summary>
public interface IRandomSource
{
    long Seed { get; }

    /// <summary>Returns a value in <c>[0, maxExclusive)</c>.</summary>
    int Next(int maxExclusive);
}
