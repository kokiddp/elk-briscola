using System.Collections.Immutable;
using Briscola.Domain.Engine;
using Briscola.Domain.Primitives;
using Briscola.Domain.State;

namespace Briscola.Domain.Tests;

public sealed class TrickResolutionTests
{
    private readonly BriscolaEngine _engine = new();

    public enum TrickCase
    {
        AllLeadSuit_NoBriscolas,
        OneBriscola,
        MultipleBriscolas,
        NoBriscolas_MixedSuits,
    }

    public static IEnumerable<object[]> TrickCases()
    {
        // Each row: scenario, briscolaSuit, leader's card, follower's card, expectedWinnerSeat.

        // Case 1: both play lead suit; no briscolas. Highest of lead wins.
        // Lead = Coppe. Briscola = Spade (irrelevant).
        // Coppe-Asso (s10) vs Coppe-Tre (s9) -> Coppe-Asso wins.
        yield return new object[]
        {
            TrickCase.AllLeadSuit_NoBriscolas,
            Suit.Spade,
            new Card(Suit.Coppe, Rank.Asso),  // leader
            new Card(Suit.Coppe, Rank.Tre),   // follower
            0, // leader (seat 0) wins
        };

        // Case 1b: leader's card is weaker — follower wins.
        yield return new object[]
        {
            TrickCase.AllLeadSuit_NoBriscolas,
            Suit.Spade,
            new Card(Suit.Coppe, Rank.Tre),
            new Card(Suit.Coppe, Rank.Asso),
            1,
        };

        // Case 2: exactly one briscola — that player wins, regardless of strength.
        // Lead = Coppe. Briscola = Bastoni.
        // Coppe-Asso (high) vs Bastoni-Due (lowest briscola) -> Bastoni-Due wins.
        yield return new object[]
        {
            TrickCase.OneBriscola,
            Suit.Bastoni,
            new Card(Suit.Coppe, Rank.Asso),
            new Card(Suit.Bastoni, Rank.Due),
            1,
        };

        // Case 2b: leader plays the briscola, follower plays a high lead-suit
        // card from a non-briscola perspective. Wait — leader's suit IS lead.
        // Adapt: leader plays a briscola (which is also the lead suit then).
        // Use leader plays Bastoni-Due (briscola, also defines lead),
        // follower plays Coppe-Asso (off-suit non-briscola). Leader still wins.
        yield return new object[]
        {
            TrickCase.OneBriscola,
            Suit.Bastoni,
            new Card(Suit.Bastoni, Rank.Due),
            new Card(Suit.Coppe, Rank.Asso),
            0,
        };

        // Case 3: multiple briscolas — highest-strength briscola wins.
        // Both play briscola (Bastoni). Leader Bastoni-Tre (s9), follower Bastoni-Asso (s10).
        yield return new object[]
        {
            TrickCase.MultipleBriscolas,
            Suit.Bastoni,
            new Card(Suit.Bastoni, Rank.Tre),
            new Card(Suit.Bastoni, Rank.Asso),
            1,
        };

        // Case 3b: leader's briscola is stronger.
        yield return new object[]
        {
            TrickCase.MultipleBriscolas,
            Suit.Bastoni,
            new Card(Suit.Bastoni, Rank.Asso),
            new Card(Suit.Bastoni, Rank.Tre),
            0,
        };

        // Case 4: no briscolas, mixed suits. Leader plays Coppe; follower plays
        // Denari (off-suit, not briscola). Briscola = Spade.
        // Off-suit non-briscola can never win — leader wins by default.
        yield return new object[]
        {
            TrickCase.NoBriscolas_MixedSuits,
            Suit.Spade,
            new Card(Suit.Coppe, Rank.Due),     // weakest lead-suit card
            new Card(Suit.Denari, Rank.Asso),   // strongest off-suit card, but cannot win
            0,
        };
    }

    [Theory]
    [MemberData(nameof(TrickCases))]
    public void Resolves_winner(
        TrickCase scenarioName,
        Suit briscolaSuit,
        Card leaderCard,
        Card followerCard,
        int expectedWinner)
    {
        _ = scenarioName;

        // Pad hands with extra cards so the game is NOT over after this trick.
        // Otherwise the score-sum-equals-120 invariant trips (correctly!) on
        // these contrived scenarios where pozzi don't contain the full deck.
        // Pad cards must not collide with the trick cards or briscola.
        var inPlay = new HashSet<Card> { leaderCard, followerCard };
        var pad0 = CardTables.FullDeck.First(c => !inPlay.Contains(c) && c.Suit != briscolaSuit);
        inPlay.Add(pad0);
        var pad1 = CardTables.FullDeck.First(c => !inPlay.Contains(c) && c.Suit != briscolaSuit);

        var state = GameTestHarness.BuildTrickScenario2p(
            briscolaSuit,
            seat0Hand: ImmutableArray.Create(leaderCard, pad0),
            seat1Hand: ImmutableArray.Create(followerCard, pad1),
            leaderSeat: 0);

        var afterLead = _engine.PlayCard(state, 0, leaderCard);
        var afterFollow = _engine.PlayCard(afterLead, 1, followerCard);

        afterFollow.Pozzi[expectedWinner].Should().Contain(new[] { leaderCard, followerCard });
        afterFollow.Pozzi[1 - expectedWinner].Should().BeEmpty();
        // Game is not yet over (each seat still holds the pad card).
        afterFollow.Phase.Should().NotBe(GamePhase.Finished);
    }

    [Theory]
    [MemberData(nameof(TrickCases))]
    public void Trick_winner_becomes_new_leader(
        TrickCase scenarioName,
        Suit briscolaSuit,
        Card leaderCard,
        Card followerCard,
        int expectedWinner)
    {
        _ = scenarioName;

        // Two cards each so the game does NOT end after the first trick;
        // we want to observe LeaderSeat after resolution.
        // Pad hands with arbitrary other cards.
        var pad0 = new Card(Suit.Spade, Rank.Quattro);
        var pad1 = new Card(Suit.Spade, Rank.Cinque);
        // Avoid clashing with the trick cards or briscola.
        // For simplicity use cards that are highly unlikely to collide.
        if (leaderCard == pad0 || followerCard == pad0) pad0 = new Card(Suit.Spade, Rank.Sei);
        if (leaderCard == pad1 || followerCard == pad1) pad1 = new Card(Suit.Spade, Rank.Sette);

        var state = GameTestHarness.BuildTrickScenario2p(
            briscolaSuit,
            seat0Hand: ImmutableArray.Create(leaderCard, pad0),
            seat1Hand: ImmutableArray.Create(followerCard, pad1),
            leaderSeat: 0);

        var after = _engine.PlayCard(_engine.PlayCard(state, 0, leaderCard), 1, followerCard);

        after.LeaderSeat.Should().Be(expectedWinner);
        after.NextToPlaySeat.Should().Be(expectedWinner);
    }

    [Fact]
    public void OffSuit_non_briscola_cannot_win_even_if_higher_strength()
    {
        var leader = new Card(Suit.Coppe, Rank.Due);
        var follower = new Card(Suit.Denari, Rank.Asso);

        // Pad so the trick doesn't end the game (avoid score-sum invariant).
        var pad0 = new Card(Suit.Bastoni, Rank.Quattro);
        var pad1 = new Card(Suit.Bastoni, Rank.Cinque);

        var state = GameTestHarness.BuildTrickScenario2p(
            Suit.Spade,
            seat0Hand: ImmutableArray.Create(leader, pad0),
            seat1Hand: ImmutableArray.Create(follower, pad1),
            leaderSeat: 0);

        var after = _engine.PlayCard(_engine.PlayCard(state, 0, leader), 1, follower);

        after.Pozzi[0].Should().Contain(leader, "leader's lead-suit card always beats off-suit non-briscolas");
        after.Pozzi[1].Should().BeEmpty();
    }

    [Fact]
    public void Lowest_briscola_beats_highest_non_briscola()
    {
        var leader = new Card(Suit.Coppe, Rank.Asso);
        var follower = new Card(Suit.Bastoni, Rank.Due);

        var pad0 = new Card(Suit.Spade, Rank.Quattro);
        var pad1 = new Card(Suit.Spade, Rank.Cinque);

        var state = GameTestHarness.BuildTrickScenario2p(
            Suit.Bastoni,
            seat0Hand: ImmutableArray.Create(leader, pad0),
            seat1Hand: ImmutableArray.Create(follower, pad1),
            leaderSeat: 0);

        var after = _engine.PlayCard(_engine.PlayCard(state, 0, leader), 1, follower);

        after.Pozzi[1].Should().Contain(follower);
        after.Pozzi[0].Should().BeEmpty();
    }
}
