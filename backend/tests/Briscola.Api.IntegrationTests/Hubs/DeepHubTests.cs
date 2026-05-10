using System.Collections.Immutable;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Briscola.Api.Dtos;
using Briscola.Api.IntegrationTests.Rest;
using Briscola.Domain.Primitives;
using Microsoft.AspNetCore.SignalR.Client;

namespace Briscola.Api.IntegrationTests.Hubs;

/// <summary>
/// Phase 5.7 deep hub-integration tests: drive a game end-to-end across
/// two real <see cref="HubConnection"/>s, exercise the disconnect grace
/// + idle-forfeit timers (real-time, with shortened
/// <c>Game:Reconnect/Idle</c> options), and assert spectator/cheating
/// guard rails. Each test owns its own <see cref="BriscolaApiFactory"/>
/// (per-fixture Postgres database) so timing-sensitive scenarios don't
/// observe each other's events.
/// </summary>
[Collection(HubTests.Name)]
public sealed class DeepHubTests : HubTestHarness
{
    [Fact]
    public async Task TwoPlayerGame_RunsToCompletion()
    {
        // Disable PlayCard rate-limiting so the auto-play loop can drive
        // the full 20-trick game without artificially slowing each play
        // by 1 s (per-connection default). The other hub tests still
        // exercise the limiter directly.
        Factory.ExtraSettings["HubRateLimits:PlayCardWindowSeconds"] = "0";

        (TokenResponse alice, TokenResponse bob, GameDetailDto game) = await SetUpRunningGameAsync();

        HubConnection aliceHub = BuildHubConnection("/hubs/game", alice.AccessToken);
        HubConnection bobHub = BuildHubConnection("/hubs/game", bob.AccessToken);

        TaskCompletionSource<GameFinishedDto> finished = new(TaskCreationOptions.RunContinuationsAsynchronously);

        aliceHub.On<RedactedStateForUserDto>("Joined", s => TryAdvance(aliceHub, 0, s));
        aliceHub.On<RedactedStateForUserDto>("StateUpdated", s => TryAdvance(aliceHub, 0, s));
        bobHub.On<RedactedStateForUserDto>("Joined", s => TryAdvance(bobHub, 1, s));
        bobHub.On<RedactedStateForUserDto>("StateUpdated", s => TryAdvance(bobHub, 1, s));
        aliceHub.On<GameFinishedDto>("GameFinished", evt => finished.TrySetResult(evt));

        await aliceHub.StartAsync();
        await bobHub.StartAsync();
        await aliceHub.InvokeAsync("JoinGame", game.Id);
        await bobHub.InvokeAsync("JoinGame", game.Id);

        GameFinishedDto result = await finished.Task.WaitAsync(TimeSpan.FromSeconds(45));

        // Game ends with a Normal reason and two seat scores summing to 120.
        result.Reason.Should().Be("Normal");
        result.SeatScores.Should().HaveCount(2);
        (result.SeatScores[0] + result.SeatScores[1]).Should().Be(120);
        result.Outcome.Kind.Should().BeOneOf("Winner", "Draw");

        await aliceHub.DisposeAsync();
        await bobHub.DisposeAsync();
    }

    [Fact]
    public async Task Disconnect_within_grace_resumes_without_forfeit()
    {
        // Default grace is plenty; the test calls Reconnect immediately,
        // so we don't need to alter the GameOptions.
        (TokenResponse alice, TokenResponse bob, GameDetailDto game) = await SetUpRunningGameAsync();

        HubConnection aliceHub = BuildHubConnection("/hubs/game", alice.AccessToken);
        HubConnection bobHub = BuildHubConnection("/hubs/game", bob.AccessToken);

        TaskCompletionSource<int> bobSawDisconnect = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource<int> bobSawReconnect = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource<GameFinishedDto> finished = new(TaskCreationOptions.RunContinuationsAsynchronously);

        bobHub.On<int, DateTimeOffset>("PlayerDisconnected",
            (seat, _) => bobSawDisconnect.TrySetResult(seat));
        bobHub.On<int>("PlayerReconnected", seat => bobSawReconnect.TrySetResult(seat));
        bobHub.On<GameFinishedDto>("GameFinished", evt => finished.TrySetResult(evt));

        await aliceHub.StartAsync();
        await bobHub.StartAsync();
        await aliceHub.InvokeAsync("JoinGame", game.Id);
        await bobHub.InvokeAsync("JoinGame", game.Id);

        // alice "leaves" the running game → DisconnectCommand → grace timer.
        await aliceHub.InvokeAsync("LeaveGame", game.Id);
        int disconnectedSeat = await bobSawDisconnect.Task.WaitAsync(TimeSpan.FromSeconds(5));
        disconnectedSeat.Should().Be(0);

        // alice rejoins immediately — grace timer should be cancelled.
        await aliceHub.InvokeAsync("JoinGame", game.Id);
        int reconnectedSeat = await bobSawReconnect.Task.WaitAsync(TimeSpan.FromSeconds(5));
        reconnectedSeat.Should().Be(0);

        // Give the (now-cancelled) grace timer a chance to fire if it
        // were going to. Default grace is 120 s so we just sleep a tick
        // and confirm no forfeit landed.
        await Task.Delay(200);
        finished.Task.IsCompleted.Should().BeFalse();

        await aliceHub.DisposeAsync();
        await bobHub.DisposeAsync();
    }

    [Fact]
    public async Task Disconnect_past_deadline_forfeits_disconnected_seat()
    {
        // Shorten the grace window so the test runs in real time.
        Factory.ExtraSettings["Game:ReconnectGraceSeconds"] = "1";

        (TokenResponse alice, TokenResponse bob, GameDetailDto game) = await SetUpRunningGameAsync();

        HubConnection aliceHub = BuildHubConnection("/hubs/game", alice.AccessToken);
        HubConnection bobHub = BuildHubConnection("/hubs/game", bob.AccessToken);

        TaskCompletionSource<GameFinishedDto> finished = new(TaskCreationOptions.RunContinuationsAsynchronously);
        bobHub.On<GameFinishedDto>("GameFinished", evt => finished.TrySetResult(evt));

        await aliceHub.StartAsync();
        await bobHub.StartAsync();
        await aliceHub.InvokeAsync("JoinGame", game.Id);
        await bobHub.InvokeAsync("JoinGame", game.Id);

        await aliceHub.InvokeAsync("LeaveGame", game.Id);

        GameFinishedDto result = await finished.Task.WaitAsync(TimeSpan.FromSeconds(10));
        result.Reason.Should().Be("ForfeitDisconnect");
        result.Outcome.Kind.Should().Be("Winner");
        // Alice (seat 0) forfeited → bob (seat 1) wins.
        result.Outcome.WinnerKey.Should().Be(1);

        await aliceHub.DisposeAsync();
        await bobHub.DisposeAsync();
    }

    [Fact]
    public async Task Idle_timeout_forfeits_idle_seat()
    {
        // Both warn + forfeit thresholds shortened so the test runs in
        // real time. Keep them >= 1 s to avoid racing with hub setup.
        Factory.ExtraSettings["Game:IdleWarnSeconds"] = "1";
        Factory.ExtraSettings["Game:IdleForfeitSeconds"] = "2";

        (TokenResponse alice, TokenResponse bob, GameDetailDto game) = await SetUpRunningGameAsync();

        HubConnection aliceHub = BuildHubConnection("/hubs/game", alice.AccessToken);
        HubConnection bobHub = BuildHubConnection("/hubs/game", bob.AccessToken);

        TaskCompletionSource<GameFinishedDto> finished = new(TaskCreationOptions.RunContinuationsAsynchronously);
        bobHub.On<GameFinishedDto>("GameFinished", evt => finished.TrySetResult(evt));

        await aliceHub.StartAsync();
        await bobHub.StartAsync();
        await aliceHub.InvokeAsync("JoinGame", game.Id);
        await bobHub.InvokeAsync("JoinGame", game.Id);

        // Neither side plays — idle timer should fire on the next-to-play
        // seat. With dealer chosen at random (2p), seat 0 or 1 leads.
        GameFinishedDto result = await finished.Task.WaitAsync(TimeSpan.FromSeconds(10));
        result.Reason.Should().Be("ForfeitIdle");
        result.Outcome.Kind.Should().Be("Winner");

        await aliceHub.DisposeAsync();
        await bobHub.DisposeAsync();
    }

    [Fact]
    public async Task Cheating_attempt_rejected_with_CardNotInHand()
    {
        (TokenResponse alice, TokenResponse bob, GameDetailDto game) = await SetUpRunningGameAsync();

        HubConnection aliceHub = BuildHubConnection("/hubs/game", alice.AccessToken);
        HubConnection bobHub = BuildHubConnection("/hubs/game", bob.AccessToken);

        TaskCompletionSource<RedactedStateForUserDto> aliceSnap =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource<RedactedStateForUserDto> bobSnap =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource<string> aliceInvalid =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        aliceHub.On<RedactedStateForUserDto>("Joined", s => aliceSnap.TrySetResult(s));
        aliceHub.On<RedactedStateForUserDto>("StateUpdated", s => aliceSnap.TrySetResult(s));
        bobHub.On<RedactedStateForUserDto>("Joined", s => bobSnap.TrySetResult(s));
        bobHub.On<RedactedStateForUserDto>("StateUpdated", s => bobSnap.TrySetResult(s));
        aliceHub.On<string>("InvalidMove", code => aliceInvalid.TrySetResult(code));

        await aliceHub.StartAsync();
        await bobHub.StartAsync();
        await aliceHub.InvokeAsync("JoinGame", game.Id);
        await bobHub.InvokeAsync("JoinGame", game.Id);

        RedactedStateForUserDto aState = await aliceSnap.Task.WaitAsync(TimeSpan.FromSeconds(5));
        RedactedStateForUserDto bState = await bobSnap.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // The active seat tries to play a card that's in the other
        // seat's hand but not in their own — that's the canonical
        // CardNotInHand cheating attempt.
        bool aliceTurn = aState.NextToPlaySeat == 0;
        HubConnection turnHub = aliceTurn ? aliceHub : bobHub;
        ImmutableArray<CardDto> myHand = (aliceTurn ? aState : bState).MyHand!.Value;
        ImmutableArray<CardDto> theirHand = (aliceTurn ? bState : aState).MyHand!.Value;
        CardDto stolen = theirHand.First(c => !myHand.Contains(c));

        TaskCompletionSource<string> invalid = aliceTurn
            ? aliceInvalid
            : new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!aliceTurn)
        {
            bobHub.On<string>("InvalidMove", code => invalid.TrySetResult(code));
        }

        await turnHub.InvokeAsync("PlayCard", game.Id, stolen);
        string code = await invalid.Task.WaitAsync(TimeSpan.FromSeconds(5));
        code.Should().Be("CardNotInHand");

        await aliceHub.DisposeAsync();
        await bobHub.DisposeAsync();
    }

    [Fact]
    public async Task ViewOwnPile_outside_LastHand_rejected()
    {
        (TokenResponse alice, TokenResponse bob, GameDetailDto game) = await SetUpRunningGameAsync();

        HubConnection aliceHub = BuildHubConnection("/hubs/game", alice.AccessToken);

        TaskCompletionSource<string> invalid =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        aliceHub.On<string>("InvalidMove", code => invalid.TrySetResult(code));

        await aliceHub.StartAsync();
        await aliceHub.InvokeAsync("JoinGame", game.Id);

        // Game just started — Phase = Playing, not LastHand → reject.
        await aliceHub.InvokeAsync("ViewOwnPile", game.Id);

        string code = await invalid.Task.WaitAsync(TimeSpan.FromSeconds(5));
        code.Should().Be("PileViewNotAllowed");

        await aliceHub.DisposeAsync();
    }

    [Fact]
    public async Task ViewOwnPile_inside_LastHand_returns_pile_via_state_updated()
    {
        // Drive the game to LastHand by playing all cards. Reuse the
        // generic auto-play loop from TwoPlayerGame_RunsToCompletion,
        // but stop and probe ViewOwnPile when we observe Phase == LastHand.
        Factory.ExtraSettings["HubRateLimits:PlayCardWindowSeconds"] = "0";

        (TokenResponse alice, TokenResponse bob, GameDetailDto game) = await SetUpRunningGameAsync();

        HubConnection aliceHub = BuildHubConnection("/hubs/game", alice.AccessToken);
        HubConnection bobHub = BuildHubConnection("/hubs/game", bob.AccessToken);

        TaskCompletionSource<RedactedStateForUserDto> aliceLastHandSnap =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource<RedactedStateForUserDto> aliceWithPozzo =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        Action<RedactedStateForUserDto> aliceHandler = s =>
        {
            if (s.Phase == GamePhase.LastHand && !aliceLastHandSnap.Task.IsCompleted)
            {
                aliceLastHandSnap.TrySetResult(s);
            }

            // The ViewOwnPile-triggered StateUpdated is the only one that
            // populates MyPozzo (the ordinary per-trick snapshots leave it
            // null per Phase 5.4). Use that as the assertion target.
            if (s.MyPozzo is { } pozzo && pozzo.Length > 0)
            {
                aliceWithPozzo.TrySetResult(s);
            }

            TryAdvance(aliceHub, 0, s);
        };
        aliceHub.On<RedactedStateForUserDto>("Joined", aliceHandler);
        aliceHub.On<RedactedStateForUserDto>("StateUpdated", aliceHandler);
        bobHub.On<RedactedStateForUserDto>("Joined", s => TryAdvance(bobHub, 1, s));
        bobHub.On<RedactedStateForUserDto>("StateUpdated", s => TryAdvance(bobHub, 1, s));

        await aliceHub.StartAsync();
        await bobHub.StartAsync();
        await aliceHub.InvokeAsync("JoinGame", game.Id);
        await bobHub.InvokeAsync("JoinGame", game.Id);

        await aliceLastHandSnap.Task.WaitAsync(TimeSpan.FromSeconds(30));

        // Now LastHand: ask for own pile. The room replies with a
        // StateUpdated where MyPozzo is populated.
        await aliceHub.InvokeAsync("ViewOwnPile", game.Id);

        RedactedStateForUserDto withPozzo = await aliceWithPozzo.Task.WaitAsync(TimeSpan.FromSeconds(10));
        withPozzo.MyPozzo.Should().NotBeNull();
        withPozzo.MyPozzo!.Value.Length.Should().BeGreaterThan(0);

        await aliceHub.DisposeAsync();
        await bobHub.DisposeAsync();
    }

    /// <summary>
    /// Auto-play helper: when a snapshot arrives that says it's our turn
    /// and our hand is non-empty, play the first card. Wraps PlayCard in
    /// a try/catch so a stray race (e.g. another invocation already in
    /// flight) doesn't tear down the test.
    /// </summary>
    /// <summary>
    /// Schedules an auto-play on a fresh task when it's <paramref name="mySeat"/>'s
    /// turn. SignalR runs client handlers on its invocation pump;
    /// awaiting <c>InvokeAsync</c> from inside a handler can deadlock
    /// the pump (the response can't be drained until the handler
    /// returns). Detaching with <c>Task.Run</c> moves the await off the
    /// pump so the next event can be dispatched while we wait for the
    /// server to ack the play.
    ///
    /// Driving by an explicit seat number (rather than inferring from
    /// the redacted snapshot) avoids the case where both seats have
    /// equal hand counts and the auto-loop would race + burn the
    /// per-connection PlayCard rate-limiter on rejections.
    /// </summary>
    private static void TryAdvance(HubConnection hub, int mySeat, RedactedStateForUserDto s)
    {
        if (s.Phase == GamePhase.Finished)
        {
            return;
        }

        if (s.MyHand is null || s.MyHand.Value.Length == 0)
        {
            return;
        }

        if (s.NextToPlaySeat != mySeat)
        {
            return;
        }

        CardDto card = s.MyHand.Value[0];
        Guid gameId = s.GameId;
        _ = Task.Run(async () =>
        {
            try
            {
                await hub.InvokeAsync("PlayCard", gameId, card);
            }
            catch
            {
                // Best-effort: the next snapshot retries.
            }
        });
    }

    private async Task<(TokenResponse alice, TokenResponse bob, GameDetailDto running)> SetUpRunningGameAsync()
    {
        TokenResponse alice = await RegisterAndLoginAsync("alice");
        TokenResponse bob = await RegisterAndLoginAsync("bob");

        using HttpClient aliceHttp = NewClient(alice.AccessToken);
        using HttpClient bobHttp = NewClient(bob.AccessToken);

        HttpResponseMessage create = await aliceHttp.PostAsJsonAsync("/api/v1/games",
            new CreateGameRequestDto(GameMode.TwoPlayer, "table", false, null));
        create.StatusCode.Should().Be(HttpStatusCode.Created);
        GameDetailDto open = (await create.Content.ReadFromJsonAsync<GameDetailDto>(TestJsonOptions.Default))!;
        HttpResponseMessage join = await bobHttp.PostAsJsonAsync(
            $"/api/v1/games/{open.Id}/join", new JoinGameRequestDto(null));
        join.StatusCode.Should().Be(HttpStatusCode.OK);
        GameDetailDto running = (await join.Content.ReadFromJsonAsync<GameDetailDto>(TestJsonOptions.Default))!;
        running.Status.Should().Be(GameStatus.Running);
        return (alice, bob, running);
    }

    private HttpClient NewClient(string accessToken)
    {
        HttpClient client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return client;
    }
}
