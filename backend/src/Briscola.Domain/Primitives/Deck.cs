namespace Briscola.Domain.Primitives;

internal static class Deck
{
    /// <summary>
    /// Returns a freshly shuffled 40-card deck using Fisher-Yates and the
    /// provided random source. The same <paramref name="rng"/> seed always
    /// produces the same shuffle (replayability invariant).
    /// </summary>
    public static Card[] Shuffled(IRandomSource rng)
    {
        ArgumentNullException.ThrowIfNull(rng);

        var deck = CardTables.FullDeck.ToArray();
        for (int i = deck.Length - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (deck[i], deck[j]) = (deck[j], deck[i]);
        }
        return deck;
    }
}
