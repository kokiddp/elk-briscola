using System.Collections.Immutable;
using Briscola.Domain.Primitives;

namespace Briscola.Domain.Tests;

public sealed class HandHelpersTests
{
    [Fact]
    public void With_then_Without_is_identity()
    {
        var hand = ImmutableArray.Create(new Card(Suit.Bastoni, Rank.Asso));
        var card = new Card(Suit.Coppe, Rank.Tre);

        var added = hand.With(card);
        var back = added.Without(card);

        added.Should().HaveCount(2);
        back.Should().Equal(hand);
    }

    [Fact]
    public void Without_missing_throws()
    {
        var hand = ImmutableArray.Create(new Card(Suit.Bastoni, Rank.Asso));
        var missing = new Card(Suit.Spade, Rank.Due);

        Action act = () => hand.Without(missing);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage($"Card {missing} not in hand");
    }

    [Fact]
    public void Contains_works_for_all_40_cards()
    {
        // Asserts that we get correct membership semantics from the
        // built-in ImmutableArray<T>.Contains, with no need for our own
        // wrapper method. (Card is a record struct, so equality is structural.)
        var hand = CardTables.FullDeck.ToImmutableArray();

        foreach (var c in CardTables.FullDeck)
        {
            hand.Contains(c).Should().BeTrue("the full deck must contain {0}", c);
        }

        hand.Contains(new Card(Suit.Bastoni, (Rank)999)).Should().BeFalse();
    }
}
