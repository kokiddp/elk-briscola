namespace Briscola.Domain.Primitives;

public static class CardTables
{
    /// <summary>
    /// Trick-taking strength of a rank within a single suit. Higher beats lower.
    /// Order: Asso (10) &gt; Tre (9) &gt; Re (8) &gt; Cavallo (7) &gt; Fante (6) &gt; 7 (5) &gt; 6 (4) &gt; 5 (3) &gt; 4 (2) &gt; 2 (1).
    /// Numeric values are arbitrary; only their ordering matters.
    /// </summary>
    public static int Strength(Rank r) => r switch
    {
        Rank.Asso => 10,
        Rank.Tre => 9,
        Rank.Re => 8,
        Rank.Cavallo => 7,
        Rank.Fante => 6,
        Rank.Sette => 5,
        Rank.Sei => 4,
        Rank.Cinque => 3,
        Rank.Quattro => 2,
        Rank.Due => 1,
        _ => throw new ArgumentOutOfRangeException(nameof(r), r, "Unknown rank"),
    };

    /// <summary>
    /// Point value of a rank as taken into a player's pozzo.
    /// Total over the full deck must equal <see cref="TotalDeckPoints"/>.
    /// </summary>
    public static int Points(Rank r) => r switch
    {
        Rank.Asso => 11,
        Rank.Tre => 10,
        Rank.Re => 4,
        Rank.Cavallo => 3,
        Rank.Fante => 2,
        Rank.Sette or Rank.Sei or Rank.Cinque or Rank.Quattro or Rank.Due => 0,
        _ => throw new ArgumentOutOfRangeException(nameof(r), r, "Unknown rank"),
    };

    /// <summary>
    /// The canonical 40-card deck: every (Suit, Rank) pair, exactly once.
    /// Order is implementation-defined; do not rely on it (use Deck.Shuffled to play).
    /// </summary>
    public static IReadOnlyList<Card> FullDeck { get; } =
        Enum.GetValues<Suit>()
            .SelectMany(s => Enum.GetValues<Rank>().Select(r => new Card(s, r)))
            .ToArray();

    public const int TotalDeckPoints = 120;
}
