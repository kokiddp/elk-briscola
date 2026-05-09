using Briscola.Application.Background;
using Briscola.Application.Configuration;
using Briscola.Application.Errors;
using Briscola.Application.Lobby;
using Briscola.Application.Orchestration;
using Briscola.Application.Persistence;
using Briscola.Application.Tests.TestDoubles;
using Briscola.Domain.Engine;
using Briscola.Domain.Primitives;
using Microsoft.Extensions.Options;

namespace Briscola.Application.Tests;

public sealed class LobbyServiceTests
{
    [Fact]
    public async Task Create_adds_creator_to_first_seat()
    {
        TestFixture fixture = new();
        Guid creator = Guid.NewGuid();

        GameRecord record = await fixture.Service.CreateAsync(
            new CreateGameRequest(GameMode.TwoPlayer, "table", IsPrivate: false, Password: null),
            creator,
            CancellationToken.None);

        record.Status.Should().Be(GameStatus.Open);
        record.SeatUserIds[0].Should().Be(creator);
        record.SeatUserIds[1].Should().BeNull();
    }

    [Fact]
    public async Task Join_starts_game_when_seats_fill()
    {
        TestFixture fixture = new();
        Guid creator = Guid.NewGuid();
        GameRecord created = await fixture.Service.CreateAsync(
            new CreateGameRequest(GameMode.TwoPlayer, "table", IsPrivate: false, Password: null),
            creator,
            CancellationToken.None);

        GameRecord joined = await fixture.Service.JoinAsync(
            created.Id,
            Guid.NewGuid(),
            password: null,
            CancellationToken.None);

        joined.Status.Should().Be(GameStatus.Running);
        joined.StateSnapshotJson.Should().NotBeEmpty();
        joined.StartedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task Concurrent_joins_for_last_seat_allow_exactly_one_winner()
    {
        TestFixture fixture = new();
        GameRecord created = await fixture.Service.CreateAsync(
            new CreateGameRequest(GameMode.TwoPlayer, "table", IsPrivate: false, Password: null),
            Guid.NewGuid(),
            CancellationToken.None);

        Task<GameRecord>[] joins =
        [
            fixture.Service.JoinAsync(created.Id, Guid.NewGuid(), null, CancellationToken.None),
            fixture.Service.JoinAsync(created.Id, Guid.NewGuid(), null, CancellationToken.None),
        ];

        try
        {
            await Task.WhenAll(joins);
        }
        catch (LobbyConflictException)
        {
        }

        joins.Count(t => t.IsCompletedSuccessfully).Should().Be(1);
    }

    [Fact]
    public async Task Cannot_leave_running_game()
    {
        TestFixture fixture = new();
        Guid creator = Guid.NewGuid();
        Guid joiner = Guid.NewGuid();
        GameRecord created = await fixture.Service.CreateAsync(
            new CreateGameRequest(GameMode.TwoPlayer, "table", IsPrivate: false, Password: null),
            creator,
            CancellationToken.None);
        await fixture.Service.JoinAsync(created.Id, joiner, null, CancellationToken.None);

        Func<Task> act = () => fixture.Service.LeaveAsync(created.Id, creator, CancellationToken.None);

        await act.Should().ThrowAsync<LobbyConflictException>();
    }

    [Fact]
    public async Task Private_game_requires_correct_password()
    {
        TestFixture fixture = new();
        GameRecord created = await fixture.Service.CreateAsync(
            new CreateGameRequest(GameMode.TwoPlayer, "private", IsPrivate: true, Password: "secret"),
            Guid.NewGuid(),
            CancellationToken.None);

        Func<Task> wrong = () => fixture.Service.JoinAsync(
            created.Id,
            Guid.NewGuid(),
            "wrong",
            CancellationToken.None);
        await wrong.Should().ThrowAsync<InvalidPasswordException>();

        GameRecord joined = await fixture.Service.JoinAsync(
            created.Id,
            Guid.NewGuid(),
            "secret",
            CancellationToken.None);
        joined.Status.Should().Be(GameStatus.Running);
    }

    [Fact]
    public async Task Open_lobby_janitor_abandons_expired_games()
    {
        TestFixture fixture = new();
        GameRecord created = await fixture.Service.CreateAsync(
            new CreateGameRequest(GameMode.TwoPlayer, "old", IsPrivate: false, Password: null),
            Guid.NewGuid(),
            CancellationToken.None);
        fixture.Clock.Advance(TimeSpan.FromMinutes(61));
        OpenLobbyJanitor janitor = new(
            fixture.Games,
            fixture.Clock,
            Options.Create(new GameOptions { OpenLobbyTtlMinutes = 60 }));

        await janitor.RunOnceAsync(CancellationToken.None);

        GameRecord? after = await fixture.Games.GetAsync(created.Id, CancellationToken.None);
        after!.Status.Should().Be(GameStatus.Abandoned);
    }

    private sealed class TestFixture
    {
        public FakeClock Clock { get; } =
            new(new DateTimeOffset(2026, 5, 9, 12, 0, 0, TimeSpan.Zero));
        public InMemoryGameRepository Games { get; } = new();
        public InMemoryGameStateCodec Codec { get; } = new();
        public LobbyService Service { get; }

        public TestFixture()
        {
            IBriscolaEngine engine = new BriscolaEngine();
            RecordingGameEventBus bus = new();
            FakeTimerService timers = new(Clock);
            GameOrchestrator orchestrator = new(
                Games,
                Codec,
                engine,
                bus,
                Clock,
                timers,
                Options.Create(new GameOptions()));
            Service = new LobbyService(
                Games,
                new FakePasswordHasher(),
                Codec,
                engine,
                new FakeRandomSource(),
                Clock,
                orchestrator);
        }
    }
}
