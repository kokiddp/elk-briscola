using Briscola.Domain.Primitives;

namespace Briscola.Domain.Tests;

public sealed class DeckTests
{
    [Fact]
    public void Shuffled_returns_40_distinct_cards()
    {
        var deck = Deck.Shuffled(new SeededRandomSource(1));

        deck.Should().HaveCount(40);
        deck.Distinct().Should().HaveCount(40);
    }

    [Fact]
    public void Shuffled_with_same_seed_is_identical()
    {
        var a = Deck.Shuffled(new SeededRandomSource(12345));
        var b = Deck.Shuffled(new SeededRandomSource(12345));

        a.Should().Equal(b);
    }

    [Fact]
    public void Shuffled_with_different_seeds_differs()
    {
        var a = Deck.Shuffled(new SeededRandomSource(1));
        var b = Deck.Shuffled(new SeededRandomSource(2));

        // Two random shuffles being identical has probability 1/40! ≈ 1.2e-48.
        // Anything other than "differs" is essentially impossible.
        a.Should().NotEqual(b);
    }

    [Fact]
    public void Shuffled_throws_on_null_rng()
    {
        Action act = () => Deck.Shuffled(null!);
        act.Should().Throw<ArgumentNullException>();
    }
}
