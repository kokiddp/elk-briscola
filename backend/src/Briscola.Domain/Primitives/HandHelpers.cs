using System.Collections.Immutable;

namespace Briscola.Domain.Primitives;

internal static class HandHelpers
{
    /// <summary>
    /// Returns a new hand with the given card removed.
    /// Throws <see cref="InvalidOperationException"/> if the card is absent;
    /// the engine catches this and surfaces an InvalidMoveException with
    /// code <c>CardNotInHand</c>.
    /// </summary>
    public static ImmutableArray<Card> Without(this ImmutableArray<Card> hand, Card c)
    {
        var i = hand.IndexOf(c);
        if (i < 0)
        {
            throw new InvalidOperationException($"Card {c} not in hand");
        }
        return hand.RemoveAt(i);
    }

    /// <summary>Returns a new hand with the given card appended.</summary>
    public static ImmutableArray<Card> With(this ImmutableArray<Card> hand, Card c)
        => hand.Add(c);
}
