using System.Collections.Immutable;
using Briscola.Application.Background;
using Briscola.Application.Configuration;
using Briscola.Application.Errors;
using Briscola.Application.Lobby;
using Briscola.Application.Orchestration;
using Briscola.Application.Persistence;
using Briscola.Application.Ranking;
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
    public async Task Each_started_game_gets_fresh_shuffle_seed()
    {
        TestFixture fixture = new();
        GameRecord first = await fixture.Service.CreateAsync(
            new CreateGameRequest(GameMode.TwoPlayer, "one", IsPrivate: false, Password: null),
            Guid.NewGuid(),
            CancellationToken.None);
        GameRecord second = await fixture.Service.CreateAsync(
            new CreateGameRequest(GameMode.TwoPlayer, "two", IsPrivate: false, Password: null),
            Guid.NewGuid(),
            CancellationToken.None);

        GameRecord firstStarted = await fixture.Service.JoinAsync(
            first.Id,
            Guid.NewGuid(),
            password: null,
            CancellationToken.None);
        GameRecord secondStarted = await fixture.Service.JoinAsync(
            second.Id,
            Guid.NewGuid(),
            password: null,
            CancellationToken.None);

        firstStarted.ShuffleSeed.Should().NotBe(secondStarted.ShuffleSeed);
    }

    [Fact]
    public async Task List_returns_game_summaries()
    {
        TestFixture fixture = new();
        await fixture.Service.CreateAsync(
            new CreateGameRequest(GameMode.FourPlayerTeams, "team table", IsPrivate: false, Password: null),
            Guid.NewGuid(),
            CancellationToken.None);

        IReadOnlyList<GameSummary> summaries =
            await fixture.Service.ListAsync(GameStatus.Open, CancellationToken.None);

        summaries.Should().ContainSingle().Which.Should().Match<GameSummary>(s =>
            s.Name == "team table"
            && s.OccupiedSeats == 1
            && s.TotalSeats == 4);
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
    public async Task Leave_open_game_clears_existing_seat()
    {
        TestFixture fixture = new();
        Guid creator = Guid.NewGuid();
        GameRecord created = await fixture.Service.CreateAsync(
            new CreateGameRequest(GameMode.TwoPlayer, "table", IsPrivate: false, Password: null),
            creator,
            CancellationToken.None);

        await fixture.Service.LeaveAsync(created.Id, creator, CancellationToken.None);

        GameRecord? after = await fixture.Games.GetAsync(created.Id, CancellationToken.None);
        after!.SeatUserIds.Should().OnlyContain(static userId => userId == null);
    }

    [Fact]
    public async Task Rejoining_same_open_game_is_idempotent()
    {
        TestFixture fixture = new();
        Guid creator = Guid.NewGuid();
        GameRecord created = await fixture.Service.CreateAsync(
            new CreateGameRequest(GameMode.TwoPlayer, "table", IsPrivate: false, Password: null),
            creator,
            CancellationToken.None);

        GameRecord joined = await fixture.Service.JoinAsync(
            created.Id,
            creator,
            password: null,
            CancellationToken.None);

        joined.SeatUserIds.Count(id => id == creator).Should().Be(1);
        joined.Status.Should().Be(GameStatus.Open);
    }

    [Fact]
    public async Task Join_full_open_record_reports_conflict()
    {
        TestFixture fixture = new();
        Guid gameId = Guid.NewGuid();
        Guid[] users = [Guid.NewGuid(), Guid.NewGuid()];
        GameRecord fullOpen = new(
            gameId,
            GameMode.TwoPlayer,
            "full",
            GameStatus.Open,
            users[0],
            fixture.Clock.UtcNow,
            StartedAt: null,
            EndedAt: null,
            ShuffleSeed: 0,
            StateSnapshotJson: string.Empty,
            BriscolaSuit: Suit.Bastoni,
            IsPrivate: false,
            PasswordHash: null,
            users.Select(u => (Guid?)u).ToImmutableArray(),
            Version: 0);
        await fixture.Games.CreateAsync(fullOpen, CancellationToken.None);

        Func<Task> act = () => fixture.Service.JoinAsync(
            gameId,
            Guid.NewGuid(),
            password: null,
            CancellationToken.None);

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
    public async Task Private_game_creation_requires_password()
    {
        TestFixture fixture = new();

        Func<Task> act = () => fixture.Service.CreateAsync(
            new CreateGameRequest(GameMode.TwoPlayer, "private", IsPrivate: true, Password: null),
            Guid.NewGuid(),
            CancellationToken.None);

        await act.Should().ThrowAsync<InvalidPasswordException>();
    }

    [Fact]
    public async Task Joining_missing_game_throws_not_found()
    {
        TestFixture fixture = new();
        Guid missingGameId = Guid.NewGuid();

        Func<Task> act = () => fixture.Service.JoinAsync(
            missingGameId,
            Guid.NewGuid(),
            password: null,
            CancellationToken.None);

        (await act.Should().ThrowAsync<GameNotFoundException>())
            .Which.GameId.Should().Be(missingGameId);
    }

    [Fact]
    public async Task Leave_by_non_seated_user_is_no_op()
    {
        TestFixture fixture = new();
        GameRecord created = await fixture.Service.CreateAsync(
            new CreateGameRequest(GameMode.TwoPlayer, "table", IsPrivate: false, Password: null),
            Guid.NewGuid(),
            CancellationToken.None);

        await fixture.Service.LeaveAsync(created.Id, Guid.NewGuid(), CancellationToken.None);

        GameRecord? after = await fixture.Games.GetAsync(created.Id, CancellationToken.None);
        after!.SeatUserIds.Count(static id => id.HasValue).Should().Be(1);
    }

    [Fact]
    public async Task Open_lobby_janitor_abandons_expired_games_and_publishes_lobby_ended()
    {
        TestFixture fixture = new();
        GameRecord created = await fixture.Service.CreateAsync(
            new CreateGameRequest(GameMode.TwoPlayer, "old", IsPrivate: false, Password: null),
            Guid.NewGuid(),
            CancellationToken.None);
        int eventsBeforeJanitor = fixture.Bus.Events.Count;
        fixture.Clock.Advance(TimeSpan.FromMinutes(61));
        OpenLobbyJanitor janitor = new(
            fixture.GamesFactory,
            fixture.Clock,
            Options.Create(new GameOptions { OpenLobbyTtlMinutes = 60 }),
            fixture.Bus);

        await janitor.RunOnceAsync(CancellationToken.None);

        GameRecord? after = await fixture.Games.GetAsync(created.Id, CancellationToken.None);
        after!.Status.Should().Be(GameStatus.Abandoned);

        // The dispatcher routes LobbyGameEndedEvent → ILobbyClient.GameEnded
        // → SPA removes the row + clears the creator's pending banner.
        Briscola.Application.Orchestration.Events.LobbyGameEndedEvent endedEvent =
            fixture.Bus.Events
                .Skip(eventsBeforeJanitor)
                .OfType<Briscola.Application.Orchestration.Events.LobbyGameEndedEvent>()
                .Single();
        endedEvent.GameId.Should().Be(created.Id);
    }

    private sealed class TestFixture
    {
        public FakeClock Clock { get; } =
            new(new DateTimeOffset(2026, 5, 9, 12, 0, 0, TimeSpan.Zero));
        public InMemoryGameRepository Games { get; } = new();
        public InMemoryGameRepositoryFactory GamesFactory { get; }
        public InMemoryGameStateCodec Codec { get; } = new();
        public LobbyService Service { get; }

        public RecordingGameEventBus Bus { get; } = new();

        public TestFixture()
        {
            InMemoryRankingRepository rankings = new(Clock.UtcNow);
            RankingService ranking = new(rankings, Clock);
            GamesFactory = new InMemoryGameRepositoryFactory(Games, ranking);
            IBriscolaEngine engine = new BriscolaEngine();
            RecordingGameEventBus bus = Bus;
            FakeTimerService timers = new(Clock);
            GameOrchestrator orchestrator = new(
                GamesFactory,
                Codec,
                engine,
                bus,
                Clock,
                timers,
                Options.Create(new GameOptions()),
                new Briscola.Application.Telemetry.BriscolaMetrics());
            Service = new LobbyService(
                Games,
                new FakePasswordHasher(),
                Codec,
                engine,
                new FakeRandomSourceFactory(),
                Clock,
                orchestrator,
                bus,
                new FakePlayerDirectory());
        }
    }
}
