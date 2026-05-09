using System.Collections.Immutable;
using Briscola.Domain.Engine;
using Briscola.Domain.Primitives;
using Briscola.Domain.State;
using Briscola.Infrastructure.Codecs;

namespace Briscola.Api.IntegrationTests.Persistence;

/// <summary>
/// Round-trips a fully populated GameState through JsonGameStateCodec and
/// asserts every field comes back identical. Closes the Phase 2 follow-up
/// "snapshot codec coverage" item — InMemoryGameStateCodec was a pass-through
/// dictionary, so until now the JSON shape was untested.
/// </summary>
public sealed class JsonGameStateCodecTests
{
    private readonly JsonGameStateCodec _codec = new();

    [Fact]
    public void Round_trip_of_fresh_2p_game_preserves_every_field()
    {
        var engine = new BriscolaEngine();
        var setup = new GameSetup(
            Guid.NewGuid(),
            GameMode.TwoPlayer,
            DealerSeat: 0,
            ImmutableArray.Create(Guid.NewGuid(), Guid.NewGuid()));

        var state = engine.StartGame(setup, new SeededRandomSource(12345));

        var json = _codec.Serialize(state);
        var roundTripped = _codec.Deserialize(json);

        roundTripped.GameId.Should().Be(state.GameId);
        roundTripped.Mode.Should().Be(state.Mode);
        roundTripped.ShuffleSeed.Should().Be(state.ShuffleSeed);
        roundTripped.DealerSeat.Should().Be(state.DealerSeat);
        roundTripped.LeaderSeat.Should().Be(state.LeaderSeat);
        roundTripped.NextToPlaySeat.Should().Be(state.NextToPlaySeat);
        roundTripped.Phase.Should().Be(state.Phase);
        roundTripped.TrickNumber.Should().Be(state.TrickNumber);
        roundTripped.BriscolaCard.Should().Be(state.BriscolaCard);
        roundTripped.BriscolaSuit.Should().Be(state.BriscolaSuit);
        roundTripped.Stock.Should().Equal(state.Stock);
        roundTripped.Hands.Should().HaveCount(2);
        for (int i = 0; i < state.Hands.Length; i++)
        {
            roundTripped.Hands[i].Should().Equal(state.Hands[i]);
        }
        roundTripped.Pozzi.Should().HaveCount(2);
        roundTripped.SeatScores.Should().Equal(state.SeatScores);
        roundTripped.Outcome.Should().Be(state.Outcome);
    }

    [Fact]
    public void Round_trip_of_finished_4p_with_winner_outcome_preserves_outcome()
    {
        var finished = PlayDeterministicGame(GameMode.FourPlayerTeams, seed: 7);
        finished.Phase.Should().Be(GamePhase.Finished);
        finished.Outcome.Should().NotBeNull();

        var json = _codec.Serialize(finished);
        var roundTripped = _codec.Deserialize(json);

        roundTripped.Outcome.Should().Be(finished.Outcome);
        roundTripped.SeatScores.Should().Equal(finished.SeatScores);
        roundTripped.Pozzi.Should().HaveCount(4);
        for (int i = 0; i < finished.Pozzi.Length; i++)
        {
            roundTripped.Pozzi[i].Should().Equal(finished.Pozzi[i]);
        }
    }

    [Fact]
    public void Round_trip_preserves_GameOutcome_Draw()
    {
        // Construct a finished state with Outcome = Draw via the engine.
        // Seed 68 with the random-mover yields 60-60 — same trick we used in
        // ScoringTests.
        var engine = new BriscolaEngine();
        var setup = new GameSetup(
            Guid.NewGuid(),
            GameMode.TwoPlayer,
            DealerSeat: 0,
            ImmutableArray.Create(Guid.NewGuid(), Guid.NewGuid()));
        var moveRng = new Random(unchecked((int)(68L ^ 0xCAFEBABEL)));
        var state = engine.StartGame(setup, new SeededRandomSource(68));
        while (state.Phase != GamePhase.Finished)
        {
            int seat = state.NextToPlaySeat;
            var hand = state.Hands[seat];
            state = engine.PlayCard(state, seat, hand[moveRng.Next(hand.Length)]);
        }
        state.Outcome.Should().BeOfType<GameOutcome.Draw>();

        var json = _codec.Serialize(state);
        var roundTripped = _codec.Deserialize(json);

        roundTripped.Outcome.Should().BeOfType<GameOutcome.Draw>();
    }

    [Fact]
    public void Serialize_throws_on_null_state()
    {
        Action act = () => _codec.Serialize(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Deserialize_throws_on_empty_input()
    {
        Action act = () => _codec.Deserialize(string.Empty);
        act.Should().Throw<ArgumentException>();
    }

    private static GameState PlayDeterministicGame(GameMode mode, long seed)
    {
        var engine = new BriscolaEngine();
        int n = mode == GameMode.TwoPlayer ? 2 : 4;
        var setup = new GameSetup(
            Guid.NewGuid(),
            mode,
            DealerSeat: 0,
            ImmutableArray.CreateRange(Enumerable.Range(0, n).Select(_ => Guid.NewGuid())));
        var state = engine.StartGame(setup, new SeededRandomSource(seed));
        while (state.Phase != GamePhase.Finished)
        {
            int seat = state.NextToPlaySeat;
            state = engine.PlayCard(state, seat, state.Hands[seat][0]);
        }
        return state;
    }
}
