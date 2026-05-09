using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Briscola.Api.Dtos;
using Briscola.Api.IntegrationTests.Rest;
using Briscola.Domain.Primitives;
using Microsoft.AspNetCore.SignalR.Client;

namespace Briscola.Api.IntegrationTests.Hubs;

[Collection(HubTests.Name)]
public sealed class GameEventDispatcherTests : HubTestHarness
{
    [Fact]
    public async Task JoinGame_delivers_a_redacted_snapshot_to_the_caller()
    {
        (TokenResponse alice, _, GameDetailDto game) = await SetUpRunningGameAsync();

        HubConnection aliceHub = BuildHubConnection("/hubs/game", alice.AccessToken);
        TaskCompletionSource<RedactedStateForUserDto> joined =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        aliceHub.On<RedactedStateForUserDto>("Joined", snap => joined.TrySetResult(snap));
        aliceHub.On<RedactedStateForUserDto>("StateUpdated", snap => joined.TrySetResult(snap));

        await aliceHub.StartAsync();
        await aliceHub.InvokeAsync("JoinGame", game.Id);

        RedactedStateForUserDto snapshot = await joined.Task.WaitAsync(TimeSpan.FromSeconds(5));
        snapshot.GameId.Should().Be(game.Id);
        snapshot.MyHand.Should().NotBeNull();
        snapshot.MyHand!.Value.Length.Should().Be(3, "Briscola starts each player with 3 cards");
        snapshot.HandCountsBySeat.Should().HaveCount(2);

        await aliceHub.DisposeAsync();
    }

    [Fact]
    public async Task JoinGame_does_not_leak_other_players_hands()
    {
        (TokenResponse alice, _, GameDetailDto game) = await SetUpRunningGameAsync();

        HubConnection aliceHub = BuildHubConnection("/hubs/game", alice.AccessToken);
        RedactedStateForUserDto? snapshot = null;
        TaskCompletionSource done = new(TaskCreationOptions.RunContinuationsAsynchronously);
        aliceHub.On<RedactedStateForUserDto>("Joined", snap => { snapshot = snap; done.TrySetResult(); });
        aliceHub.On<RedactedStateForUserDto>("StateUpdated", snap => { snapshot = snap; done.TrySetResult(); });

        await aliceHub.StartAsync();
        await aliceHub.InvokeAsync("JoinGame", game.Id);
        await done.Task.WaitAsync(TimeSpan.FromSeconds(5));

        snapshot.Should().NotBeNull();
        // Per-recipient redaction: the snapshot includes alice's hand
        // but only counts for everyone else. The wire shape mirrors the
        // application-layer RedactedStateForUser; HandCountsBySeat is
        // the only window into other players' hand sizes.
        snapshot!.MyHand.Should().NotBeNull();
        snapshot.HandCountsBySeat.All(c => c == 3).Should().BeTrue();

        await aliceHub.DisposeAsync();
    }

    [Fact]
    public async Task PlayerReconnected_broadcast_reaches_other_seat()
    {
        (TokenResponse alice, TokenResponse bob, GameDetailDto game) = await SetUpRunningGameAsync();

        HubConnection aliceHub = BuildHubConnection("/hubs/game", alice.AccessToken);
        HubConnection bobHub = BuildHubConnection("/hubs/game", bob.AccessToken);

        TaskCompletionSource<int> bobSawAliceReconnect =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        bobHub.On<int>("PlayerReconnected", seatIndex => bobSawAliceReconnect.TrySetResult(seatIndex));

        await aliceHub.StartAsync();
        await bobHub.StartAsync();

        // Bob has to be in the group to see the broadcast.
        await bobHub.InvokeAsync("JoinGame", game.Id);

        // Simulate alice disconnecting then reconnecting through the
        // hub. JoinGame after a Disconnect command emits a
        // PlayerReconnectedEvent which the dispatcher fans out to the
        // game group.
        await aliceHub.InvokeAsync("JoinGame", game.Id);          // initial Joined (no reconnect)
        await aliceHub.InvokeAsync("LeaveGame", game.Id);         // enqueues Disconnect
        await aliceHub.InvokeAsync("JoinGame", game.Id);          // enqueues Reconnect → broadcast

        int seat = await bobSawAliceReconnect.Task.WaitAsync(TimeSpan.FromSeconds(5));
        // Either seat could be alice's — the orchestrator picks the
        // dealer at random. Just assert the callback fired with one of
        // the two valid seats.
        seat.Should().BeOneOf(0, 1);

        await aliceHub.DisposeAsync();
        await bobHub.DisposeAsync();
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
