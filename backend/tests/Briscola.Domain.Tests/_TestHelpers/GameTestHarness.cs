using System.Collections.Immutable;
using Briscola.Domain.Engine;
using Briscola.Domain.Primitives;
using Briscola.Domain.State;

namespace Briscola.Domain.Tests;

/// <summary>
/// Shared test scaffolding. Keeps the named-test files focused on
/// assertions rather than setup boilerplate.
/// </summary>
internal static class GameTestHarness
{
    public static GameSetup Setup2p(int dealerSeat = 0, Guid? gameId = null) => new(
        gameId ?? Guid.NewGuid(),
        GameMode.TwoPlayer,
        DealerSeat: dealerSeat,
        PlayerIds: ImmutableArray.Create(Guid.NewGuid(), Guid.NewGuid()));

    public static GameSetup Setup4p(int dealerSeat = 0, Guid? gameId = null) => new(
        gameId ?? Guid.NewGuid(),
        GameMode.FourPlayerTeams,
        DealerSeat: dealerSeat,
        PlayerIds: ImmutableArray.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()));

    public static GameSetup SetupForMode(GameMode mode, int dealerSeat = 0) =>
        mode == GameMode.TwoPlayer ? Setup2p(dealerSeat) : Setup4p(dealerSeat);

    public static int PlayerCount(GameMode mode) =>
        mode == GameMode.TwoPlayer ? 2 : 4;

    /// <summary>
    /// Play a full game using a deterministic move-picker (always plays the
    /// hand[0] card on each turn). Returns the final state.
    /// </summary>
    public static GameState PlayDeterministicGame(GameMode mode, long seed, int dealerSeat = 0)
    {
        var engine = new BriscolaEngine();
        var state = engine.StartGame(SetupForMode(mode, dealerSeat), new SeededRandomSource(seed));
        int safety = 200;
        while (state.Phase != GamePhase.Finished)
        {
            if (--safety < 0) throw new InvalidOperationException("did not terminate");
            int seat = state.NextToPlaySeat;
            state = engine.PlayCard(state, seat, state.Hands[seat][0]);
        }
        return state;
    }

    /// <summary>
    /// Play a full game using a deterministic random move-picker. The seed
    /// for the move-picker is derived from the deal seed XOR a constant so
    /// the play strategy is independent of the deal but still deterministic.
    /// </summary>
    public static GameState PlayRandomLegalGame(GameMode mode, long seed, int dealerSeat = 0)
    {
        var engine = new BriscolaEngine();
        var moveRng = new Random(unchecked((int)(seed ^ 0xCAFEBABEL)));
        var state = engine.StartGame(SetupForMode(mode, dealerSeat), new SeededRandomSource(seed));
        int safety = 200;
        while (state.Phase != GamePhase.Finished)
        {
            if (--safety < 0) throw new InvalidOperationException("did not terminate");
            int seat = state.NextToPlaySeat;
            var hand = state.Hands[seat];
            state = engine.PlayCard(state, seat, hand[moveRng.Next(hand.Length)]);
        }
        return state;
    }

    /// <summary>
    /// Build a 2p mid-game state with a chosen briscola suit and chosen
    /// hand contents for each seat. Pozzi/scores empty, stock empty, ready
    /// to play out the current trick. Useful for testing trick resolution
    /// in isolation without going through StartGame's RNG.
    /// </summary>
    public static GameState BuildTrickScenario2p(
        Suit briscolaSuit,
        ImmutableArray<Card> seat0Hand,
        ImmutableArray<Card> seat1Hand,
        int leaderSeat = 0)
    {
        // Pick any briscola card with the chosen suit that's not in either hand.
        var inHand = seat0Hand.Concat(seat1Hand).ToHashSet();
        var briscolaCard = CardTables.FullDeck.First(c => c.Suit == briscolaSuit && !inHand.Contains(c));

        return new GameState
        {
            GameId = Guid.NewGuid(),
            Mode = GameMode.TwoPlayer,
            ShuffleSeed = 0,
            DealerSeat = (leaderSeat + 1) % 2,
            Hands = ImmutableArray.Create(seat0Hand, seat1Hand),
            Pozzi = ImmutableArray.Create(ImmutableArray<Card>.Empty, ImmutableArray<Card>.Empty),
            Stock = ImmutableArray<Card>.Empty,
            BriscolaCard = briscolaCard,
            BriscolaSuit = briscolaSuit,
            CurrentTrick = ImmutableArray<PlayedCard>.Empty,
            LeaderSeat = leaderSeat,
            NextToPlaySeat = leaderSeat,
            Phase = GamePhase.LastHand,
            TrickNumber = 12, // arbitrary, just past mid-game
            SeatScores = ImmutableArray.Create(0, 0),
            Outcome = null,
        };
    }
}
