namespace Briscola.Domain.State;

/// <summary>
/// Result of a finished game. Use pattern matching to discriminate:
/// <code>
/// switch (state.Outcome) {
///   case GameOutcome.Winner w: ...; break;
///   case GameOutcome.Draw:     ...; break;
///   case null:                 /* still running */ break;
/// }
/// </code>
/// </summary>
public abstract record GameOutcome
{
    private GameOutcome() { }

    /// <summary>
    /// In 2-player games, <c>SeatOrTeam</c> is the winning seat index (0 or 1).
    /// In 4-player team games, it is the winning team id (0 = seats {0,2}, 1 = seats {1,3}).
    /// </summary>
    public sealed record Winner(int SeatOrTeam) : GameOutcome;

    public sealed record Draw : GameOutcome;
}
