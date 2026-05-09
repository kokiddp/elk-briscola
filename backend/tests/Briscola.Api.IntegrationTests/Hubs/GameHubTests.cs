using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Briscola.Api.Dtos;
using Briscola.Api.IntegrationTests.Rest;
using Briscola.Domain.Primitives;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;

namespace Briscola.Api.IntegrationTests.Hubs;

[Collection(HubTests.Name)]
public sealed class GameHubTests : HubTestHarness
{
    [Fact]
    public async Task JoinGame_for_a_running_game_succeeds_for_a_participant()
    {
        (TokenResponse alice, TokenResponse bob, GameDetailDto game) = await SetUpRunningGame();

        HubConnection aliceHub = BuildHubConnection("/hubs/game", alice.AccessToken);
        await aliceHub.StartAsync();

        await aliceHub.InvokeAsync("JoinGame", game.Id);

        await aliceHub.DisposeAsync();
    }

    [Fact]
    public async Task JoinGame_rejects_a_non_participant()
    {
        (_, _, GameDetailDto game) = await SetUpRunningGame();
        TokenResponse stranger = await RegisterAndLoginAsync("eve");

        HubConnection eveHub = BuildHubConnection("/hubs/game", stranger.AccessToken);
        await eveHub.StartAsync();

        Func<Task> act = () => eveHub.InvokeAsync("JoinGame", game.Id);
        await act.Should().ThrowAsync<HubException>()
            .WithMessage("*not a participant*");

        await eveHub.DisposeAsync();
    }

    [Fact]
    public async Task SendChat_round_trips_to_other_participants()
    {
        (TokenResponse alice, TokenResponse bob, GameDetailDto game) = await SetUpRunningGame();

        HubConnection aliceHub = BuildHubConnection("/hubs/game", alice.AccessToken);
        HubConnection bobHub = BuildHubConnection("/hubs/game", bob.AccessToken);

        TaskCompletionSource<GameChatMessageDto> bobReceived =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        bobHub.On<GameChatMessageDto>("ChatMessage", msg => bobReceived.TrySetResult(msg));

        await aliceHub.StartAsync();
        await bobHub.StartAsync();
        await aliceHub.InvokeAsync("JoinGame", game.Id);
        await bobHub.InvokeAsync("JoinGame", game.Id);

        await aliceHub.InvokeAsync("SendChat", game.Id, "good luck");
        GameChatMessageDto delivered = await bobReceived.Task.WaitAsync(TimeSpan.FromSeconds(5));

        delivered.GameId.Should().Be(game.Id);
        delivered.Text.Should().Be("good luck");
        delivered.FromUserName.Should().Be("alice");

        await aliceHub.DisposeAsync();
        await bobHub.DisposeAsync();
    }

    [Fact]
    public async Task SendChat_drops_whitespace_only_messages()
    {
        (TokenResponse alice, TokenResponse bob, GameDetailDto game) = await SetUpRunningGame();

        HubConnection aliceHub = BuildHubConnection("/hubs/game", alice.AccessToken);
        HubConnection bobHub = BuildHubConnection("/hubs/game", bob.AccessToken);

        bool received = false;
        bobHub.On<GameChatMessageDto>("ChatMessage", _ => { received = true; });

        await aliceHub.StartAsync();
        await bobHub.StartAsync();
        await aliceHub.InvokeAsync("JoinGame", game.Id);
        await bobHub.InvokeAsync("JoinGame", game.Id);

        await aliceHub.InvokeAsync("SendChat", game.Id, "   ");
        await Task.Delay(200);
        received.Should().BeFalse();

        await aliceHub.DisposeAsync();
        await bobHub.DisposeAsync();
    }

    [Fact]
    public async Task LeaveGame_on_open_game_delegates_to_lobby_service()
    {
        TokenResponse alice = await RegisterAndLoginAsync("alice");
        using HttpClient aliceHttp = NewClient(alice.AccessToken);
        GameDetailDto open = await CreateOpenGameAsync(aliceHttp);

        HubConnection aliceHub = BuildHubConnection("/hubs/game", alice.AccessToken);
        await aliceHub.StartAsync();

        await aliceHub.InvokeAsync("LeaveGame", open.Id);

        // Seat 0 (alice) is now vacant.
        GameDetailDto after = (await aliceHttp.GetFromJsonAsync<GameDetailDto>(
            $"/api/v1/games/{open.Id}", TestJsonOptions.Default))!;
        after.Status.Should().Be(GameStatus.Open);
        after.Seats[0].Should().BeNull();

        await aliceHub.DisposeAsync();
    }

    [Fact]
    public async Task LeaveGame_on_running_game_does_not_remove_seat()
    {
        // For a running game the hub enqueues DisconnectCommand instead
        // of mutating seats. The seat list stays as-is (the orchestrator
        // is what would later forfeit on the grace timer; that's covered
        // in Phase 5.7's deeper hub-test suite).
        (TokenResponse alice, TokenResponse bob, GameDetailDto game) = await SetUpRunningGame();
        using HttpClient aliceHttp = NewClient(alice.AccessToken);

        HubConnection aliceHub = BuildHubConnection("/hubs/game", alice.AccessToken);
        await aliceHub.StartAsync();
        await aliceHub.InvokeAsync("JoinGame", game.Id);
        await aliceHub.InvokeAsync("LeaveGame", game.Id);

        GameDetailDto after = (await aliceHttp.GetFromJsonAsync<GameDetailDto>(
            $"/api/v1/games/{game.Id}", TestJsonOptions.Default))!;
        after.Status.Should().Be(GameStatus.Running);
        after.Seats.Should().AllSatisfy(seat => seat.HasValue.Should().BeTrue());

        await aliceHub.DisposeAsync();
    }

    private async Task<(TokenResponse alice, TokenResponse bob, GameDetailDto running)> SetUpRunningGame()
    {
        TokenResponse alice = await RegisterAndLoginAsync("alice");
        TokenResponse bob = await RegisterAndLoginAsync("bob");

        using HttpClient aliceHttp = NewClient(alice.AccessToken);
        using HttpClient bobHttp = NewClient(bob.AccessToken);

        GameDetailDto open = await CreateOpenGameAsync(aliceHttp);
        HttpResponseMessage join = await bobHttp.PostAsJsonAsync(
            $"/api/v1/games/{open.Id}/join", new JoinGameRequestDto(null));
        join.StatusCode.Should().Be(HttpStatusCode.OK);
        GameDetailDto running = (await join.Content.ReadFromJsonAsync<GameDetailDto>(TestJsonOptions.Default))!;
        running.Status.Should().Be(GameStatus.Running);
        return (alice, bob, running);
    }

    private static async Task<GameDetailDto> CreateOpenGameAsync(HttpClient client)
    {
        HttpResponseMessage create = await client.PostAsJsonAsync("/api/v1/games",
            new CreateGameRequestDto(GameMode.TwoPlayer, "table", false, null));
        create.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await create.Content.ReadFromJsonAsync<GameDetailDto>(TestJsonOptions.Default))!;
    }

    private HttpClient NewClient(string accessToken)
    {
        HttpClient client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return client;
    }
}
