using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using Briscola.Application.Background;
using Briscola.Application.Configuration;
using Briscola.Application.Persistence;
using Briscola.Application.Ports;
using Briscola.Application.Tests.TestDoubles;
using Briscola.Domain.Primitives;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Briscola.Application.Tests;

/// <summary>
/// In-process unit coverage for <see cref="OpenLobbyJanitor"/>'s SQL-side
/// cutoff filter (audit H9) and per-tick cap. The wire-level test in
/// Briscola.Api.IntegrationTests covers the publish + status-flip end-to-
/// end against real Postgres; these tests focus on the predicate logic
/// that used to live in <c>.Where(...)</c> client-side.
/// </summary>
public sealed class OpenLobbyJanitorTests
{
    private static GameRecord OpenGame(Guid id, DateTimeOffset createdAt) => new(
        id,
        GameMode.TwoPlayer,
        "test",
        GameStatus.Open,
        Guid.NewGuid(),
        createdAt,
        StartedAt: null,
        EndedAt: null,
        ShuffleSeed: 0,
        StateSnapshotJson: "{}",
        BriscolaSuit: Suit.Bastoni,
        IsPrivate: false,
        PasswordHash: null,
        SeatUserIds: ImmutableArray.Create<Guid?>(Guid.NewGuid(), null),
        Version: 0);

    [Fact]
    public async Task Only_processes_games_older_than_the_configured_TTL()
    {
        TestGameFactory tgf = new();
        FakeClockAdapter clock = new(tgf.Clock.UtcNow);

        // Two games: one old enough to be expired (created 10 min ago,
        // TTL is 5 min) and one too fresh (created 1 min ago).
        Guid expiredId = Guid.NewGuid();
        Guid freshId = Guid.NewGuid();
        await tgf.Games.CreateAsync(
            OpenGame(expiredId, clock.UtcNow.AddMinutes(-10)),
            CancellationToken.None);
        await tgf.Games.CreateAsync(
            OpenGame(freshId, clock.UtcNow.AddMinutes(-1)),
            CancellationToken.None);

        InMemoryGameEventBus bus = new();
        IOptions<GameOptions> opts = Options.Create(new GameOptions { OpenLobbyTtlMinutes = 5 });
        OpenLobbyJanitor janitor = new(tgf.GamesFactory, clock, opts, bus);

        await janitor.RunOnceAsync(CancellationToken.None);

        // The expired game flipped to Abandoned; the fresh one is untouched.
        GameRecord? expired = await tgf.Games.GetAsync(expiredId, CancellationToken.None);
        GameRecord? fresh = await tgf.Games.GetAsync(freshId, CancellationToken.None);
        expired!.Status.Should().Be(GameStatus.Abandoned);
        fresh!.Status.Should().Be(GameStatus.Open);

        // GameEnded was published exactly once — for the expired game.
        bus.Drain().OfType<Briscola.Application.Orchestration.Events.LobbyGameEndedEvent>()
            .Should().ContainSingle(e => e.GameId == expiredId);
    }

    [Fact]
    public async Task Caps_per_tick_so_a_backlog_doesnt_fan_out_unbounded()
    {
        TestGameFactory tgf = new();
        FakeClockAdapter clock = new(tgf.Clock.UtcNow);

        // 600 expired open games — over the 500-per-tick cap.
        for (int i = 0; i < 600; i++)
        {
            await tgf.Games.CreateAsync(
                OpenGame(Guid.NewGuid(), clock.UtcNow.AddMinutes(-10)),
                CancellationToken.None);
        }

        InMemoryGameEventBus bus = new();
        IOptions<GameOptions> opts = Options.Create(new GameOptions { OpenLobbyTtlMinutes = 5 });
        OpenLobbyJanitor janitor = new(tgf.GamesFactory, clock, opts, bus);

        await janitor.RunOnceAsync(CancellationToken.None);

        // Exactly 500 processed this tick — the remaining 100 wait for the next.
        bus.Drain().OfType<Briscola.Application.Orchestration.Events.LobbyGameEndedEvent>()
            .Should().HaveCount(500);
    }

    private sealed class FakeClockAdapter(DateTimeOffset start) : IClock
    {
        public DateTimeOffset UtcNow { get; } = start;
    }

    private sealed class InMemoryGameEventBus : IGameEventBus
    {
        private readonly List<Briscola.Application.Orchestration.Events.IGameEvent> _events = [];

        public ValueTask PublishAsync(Briscola.Application.Orchestration.Events.IGameEvent evt, CancellationToken ct = default)
        {
            lock (_events) { _events.Add(evt); }
            return ValueTask.CompletedTask;
        }

        // ReadAllAsync is unused by the janitor; provide a never-completing
        // stream so an accidental consumer would block rather than NRE.
        public async IAsyncEnumerable<Briscola.Application.Orchestration.Events.IGameEvent> ReadAllAsync(
            [EnumeratorCancellation] CancellationToken ct)
        {
            await Task.Delay(Timeout.Infinite, ct).ConfigureAwait(false);
            yield break;
        }

        public Briscola.Application.Orchestration.Events.IGameEvent[] Drain()
        {
            lock (_events) { return _events.ToArray(); }
        }
    }
}
