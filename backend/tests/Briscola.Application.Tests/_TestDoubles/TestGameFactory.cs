using System.Collections.Immutable;
using Briscola.Application.Configuration;
using Briscola.Application.Orchestration;
using Briscola.Application.Persistence;
using Briscola.Domain.Engine;
using Briscola.Domain.Primitives;
using Briscola.Domain.State;
using Microsoft.Extensions.Options;

namespace Briscola.Application.Tests.TestDoubles;

internal sealed class TestGameFactory
{
    public FakeClock Clock { get; } = new(new DateTimeOffset(2026, 5, 9, 12, 0, 0, TimeSpan.Zero));
    public InMemoryGameRepository Games { get; } = new();
    public InMemoryGameRepositoryFactory GamesFactory { get; }
    public InMemoryGameStateCodec Codec { get; } = new();
    public RecordingGameEventBus Bus { get; } = new();
    public FakeRandomSource Random { get; } = new();
    public IBriscolaEngine Engine { get; } = new BriscolaEngine();
    public FakeTimerService Timers { get; }
    public GameOptions Options { get; } = new()
    {
        ReconnectGraceSeconds = 10,
        IdleWarnSeconds = 5,
        IdleForfeitSeconds = 10,
    };

    public TestGameFactory()
    {
        Timers = new FakeTimerService(Clock);
        GamesFactory = new InMemoryGameRepositoryFactory(Games);
    }

    public async Task<(GameRoom Room, GameRecord Record, Guid[] Users)> CreateRunningRoomAsync(
        GameMode mode = GameMode.TwoPlayer)
    {
        int playerCount = mode == GameMode.TwoPlayer ? 2 : 4;
        Guid[] users = Enumerable.Range(0, playerCount).Select(_ => Guid.NewGuid()).ToArray();
        GameState state = Engine.StartGame(
            new GameSetup(Guid.NewGuid(), mode, DealerSeat: 0, users.ToImmutableArray()),
            Random);
        GameRecord record = new(
            state.GameId,
            mode,
            "test game",
            GameStatus.Running,
            users[0],
            Clock.UtcNow,
            Clock.UtcNow,
            EndedAt: null,
            state.ShuffleSeed,
            Codec.Serialize(state),
            state.BriscolaSuit,
            IsPrivate: false,
            PasswordHash: null,
            users.Select(u => (Guid?)u).ToImmutableArray(),
            Version: 0);

        await Games.CreateAsync(record, CancellationToken.None);
        GameRoom room = GameRoom.FromRecord(
            record,
            GamesFactory,
            Codec,
            Engine,
            Bus,
            Clock,
            Timers,
            OptionsWrapper());
        return (room, record, users);
    }

    public IOptions<GameOptions> OptionsWrapper() => Microsoft.Extensions.Options.Options.Create(Options);
}
