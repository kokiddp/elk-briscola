using System.Collections.Immutable;
using Briscola.Domain.Primitives;

namespace Briscola.Domain.State;

/// <summary>
/// Authoritative snapshot of a game. Engine transitions are pure functions
/// that take a <see cref="GameState"/> and return a new one — never mutate.
/// </summary>
public sealed record GameState
{
    public required Guid GameId { get; init; }
    public required GameMode Mode { get; init; }
    public required long ShuffleSeed { get; init; }
    public required int DealerSeat { get; init; }

    /// <summary>Per-seat hands, indexed by seat index.</summary>
    public required ImmutableArray<ImmutableArray<Card>> Hands { get; init; }

    /// <summary>Per-seat captured piles ("pozzi"), indexed by seat index.</summary>
    public required ImmutableArray<ImmutableArray<Card>> Pozzi { get; init; }

    /// <summary>
    /// Remaining stock to draw from. The briscola card sits at the tail
    /// (<c>Stock[^1]</c>) until drawn — by construction it is the last card
    /// taken from the stock.
    /// </summary>
    public required ImmutableArray<Card> Stock { get; init; }

    /// <summary>
    /// The trump card revealed at deal time. Always shown to all players,
    /// even after it has been drawn from the stock; clients display it as a
    /// constant indicator. Its <see cref="Card.Suit"/> equals <see cref="BriscolaSuit"/>.
    /// </summary>
    public required Card BriscolaCard { get; init; }

    public required Suit BriscolaSuit { get; init; }

    /// <summary>Cards played in the current trick, in play order (leader first).</summary>
    public required ImmutableArray<PlayedCard> CurrentTrick { get; init; }

    /// <summary>Seat that led the current trick.</summary>
    public required int LeaderSeat { get; init; }

    /// <summary>Seat that should play next.</summary>
    public required int NextToPlaySeat { get; init; }

    public required GamePhase Phase { get; init; }

    /// <summary>Number of completed tricks. Starts at 0; incremented after resolution.</summary>
    public required int TrickNumber { get; init; }

    /// <summary>Per-seat point totals. Recomputed from <see cref="Pozzi"/> after each trick.</summary>
    public required ImmutableArray<int> SeatScores { get; init; }

    /// <summary>Set only when <see cref="Phase"/> is <see cref="GamePhase.Finished"/>.</summary>
    public GameOutcome? Outcome { get; init; }
}
