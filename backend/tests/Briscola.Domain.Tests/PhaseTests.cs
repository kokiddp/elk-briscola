using Briscola.Domain.Engine;
using Briscola.Domain.Primitives;
using Briscola.Domain.State;

namespace Briscola.Domain.Tests;

public sealed class PhaseTests
{
    private readonly BriscolaEngine _engine = new();

    [Fact]
    public void Phase_is_LastHand_after_stock_and_briscola_drawn()
    {
        // Drive a 2p game forward until Stock empties; assert Phase == LastHand
        // and every seat holds exactly 3 cards at that moment.
        var s = _engine.StartGame(GameTestHarness.Setup2p(), new SeededRandomSource(31));

        bool sawLastHand = false;
        while (s.Phase != GamePhase.Finished)
        {
            int seat = s.NextToPlaySeat;
            s = _engine.PlayCard(s, seat, s.Hands[seat][0]);

            if (s.Phase == GamePhase.LastHand && !sawLastHand)
            {
                sawLastHand = true;
                s.Stock.Should().BeEmpty("LastHand begins exactly when the stock is empty");
                s.Hands.Should().OnlyContain(h => h.Length == 3,
                    "every seat must hold exactly 3 cards at the start of LastHand");
            }
        }
        sawLastHand.Should().BeTrue("the game must pass through LastHand on its way to Finished");
    }

    [Fact]
    public void Phase_is_Finished_after_all_hands_empty()
    {
        var s = GameTestHarness.PlayDeterministicGame(GameMode.TwoPlayer, seed: 1);

        s.Phase.Should().Be(GamePhase.Finished);
        s.Hands.Should().OnlyContain(h => h.Length == 0);
        s.Stock.Should().BeEmpty();
        s.Outcome.Should().NotBeNull();
    }

    [Fact]
    public void Phase_is_Playing_otherwise()
    {
        var s = _engine.StartGame(GameTestHarness.Setup2p(), new SeededRandomSource(1));
        s.Phase.Should().Be(GamePhase.Playing);

        // Play one card; trick still in progress, phase still Playing.
        int seat = s.NextToPlaySeat;
        var after = _engine.PlayCard(s, seat, s.Hands[seat][0]);
        after.Phase.Should().Be(GamePhase.Playing);
        after.CurrentTrick.Should().HaveCount(1);
    }

    [Fact]
    public void LastHand_persists_until_hands_empty()
    {
        // Once Phase enters LastHand, it should stay LastHand on subsequent
        // tricks until the very last play, where it transitions to Finished.
        var s = _engine.StartGame(GameTestHarness.Setup2p(), new SeededRandomSource(31));
        var phasesObserved = new List<GamePhase> { s.Phase };
        while (s.Phase != GamePhase.Finished)
        {
            int seat = s.NextToPlaySeat;
            s = _engine.PlayCard(s, seat, s.Hands[seat][0]);
            if (phasesObserved.Last() != s.Phase)
            {
                phasesObserved.Add(s.Phase);
            }
        }

        // Expected phase sequence: Playing -> LastHand -> Finished.
        phasesObserved.Should().Equal(GamePhase.Playing, GamePhase.LastHand, GamePhase.Finished);
    }
}
