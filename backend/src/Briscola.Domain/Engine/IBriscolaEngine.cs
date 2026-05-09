using Briscola.Domain.Primitives;
using Briscola.Domain.State;

namespace Briscola.Domain.Engine;

public interface IBriscolaEngine
{
    /// <summary>
    /// Deals a fresh game from the given setup, using <paramref name="rng"/>
    /// to shuffle. The returned state has Phase == Playing.
    /// </summary>
    GameState StartGame(GameSetup setup, IRandomSource rng);

    /// <summary>
    /// Applies a card play. Throws <see cref="Errors.InvalidMoveException"/>
    /// if the move is illegal. Returns the new state, possibly with a
    /// resolved trick, drawn cards, advanced phase, or computed outcome.
    /// </summary>
    GameState PlayCard(GameState state, int seatIndex, Card card);

    /// <summary>True iff <see cref="PlayCard"/> would NOT throw.</summary>
    bool IsLegalMove(GameState state, int seatIndex, Card card);
}
