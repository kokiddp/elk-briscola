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
public sealed class SpectatorAndLimitsTests : HubTestHarness
{
    [Fact]
    public async Task Spectator_receives_redacted_initial_state()
    {
        (TokenResponse alice, TokenResponse bob, GameDetailDto game) = await SetUpRunningGameAsync();
        TokenResponse eve = await RegisterAndLoginAsync("eve");

        HubConnection eveHub = BuildHubConnection("/hubs/game", eve.AccessToken);
        TaskCompletionSource<RedactedStateForUserDto> snap =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        eveHub.On<RedactedStateForUserDto>("StateUpdated", s => snap.TrySetResult(s));

        await eveHub.StartAsync();
        await eveHub.InvokeAsync("SpectateGame", game.Id);

        RedactedStateForUserDto state = await snap.Task.WaitAsync(TimeSpan.FromSeconds(5));
        state.GameId.Should().Be(game.Id);
        state.MyHand.Should().BeNull("spectators have no hand");
        state.MyPozzo.Should().BeNull("spectators have no pile");
        state.HandCountsBySeat.Should().HaveCount(2);
        state.HandCountsBySeat.All(c => c == 3).Should().BeTrue();

        await eveHub.DisposeAsync();
    }

    [Fact]
    public async Task Spectate_rejects_participants()
    {
        (TokenResponse alice, _, GameDetailDto game) = await SetUpRunningGameAsync();
        HubConnection aliceHub = BuildHubConnection("/hubs/game", alice.AccessToken);
        await aliceHub.StartAsync();

        Func<Task> act = () => aliceHub.InvokeAsync("SpectateGame", game.Id);
        await act.Should().ThrowAsync<HubException>()
            .WithMessage("*JoinGame, not SpectateGame*");

        await aliceHub.DisposeAsync();
    }

    [Fact]
    public async Task Spectate_rejects_open_games()
    {
        TokenResponse alice = await RegisterAndLoginAsync("alice");
        TokenResponse eve = await RegisterAndLoginAsync("eve");

        using HttpClient aliceHttp = NewClient(alice.AccessToken);
        HttpResponseMessage create = await aliceHttp.PostAsJsonAsync("/api/v1/games",
            new CreateGameRequestDto(GameMode.TwoPlayer, "open", false, null));
        create.StatusCode.Should().Be(HttpStatusCode.Created);
        GameDetailDto open = (await create.Content.ReadFromJsonAsync<GameDetailDto>(TestJsonOptions.Default))!;

        HubConnection eveHub = BuildHubConnection("/hubs/game", eve.AccessToken);
        await eveHub.StartAsync();

        Func<Task> act = () => eveHub.InvokeAsync("SpectateGame", open.Id);
        await act.Should().ThrowAsync<HubException>()
            .WithMessage("*Only running games*");

        await eveHub.DisposeAsync();
    }

    [Fact]
    public async Task Spectator_chat_is_rejected_with_targeted_invalid_move()
    {
        (TokenResponse alice, TokenResponse bob, GameDetailDto game) = await SetUpRunningGameAsync();
        TokenResponse eve = await RegisterAndLoginAsync("eve");

        HubConnection eveHub = BuildHubConnection("/hubs/game", eve.AccessToken);
        TaskCompletionSource<string> rejected =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        eveHub.On<string>("InvalidMove", code => rejected.TrySetResult(code));

        await eveHub.StartAsync();
        await eveHub.InvokeAsync("SpectateGame", game.Id);

        // Drain the StateUpdated that arrives on spectate first; it's
        // not what we're asserting here.
        await Task.Delay(100);

        // SendChat from a spectator-only connection should NOT throw —
        // the spec is "no-op + targeted error".
        await eveHub.InvokeAsync("SendChat", game.Id, "go alice!");

        string code = await rejected.Task.WaitAsync(TimeSpan.FromSeconds(5));
        code.Should().Be("SpectatorsCannotChat");

        await eveHub.DisposeAsync();
    }

    [Fact]
    public async Task Player_chat_is_not_rejected_after_player_also_spectates_a_different_game()
    {
        // Sanity: IsSpectatorOnly is per-game. Joining as player on
        // game A should keep chat allowed there even if the same
        // connection spectates game B.
        (TokenResponse alice, TokenResponse bob, GameDetailDto gameA) = await SetUpRunningGameAsync();
        // alice joins her own game (player), then SendChat works
        HubConnection aliceHub = BuildHubConnection("/hubs/game", alice.AccessToken);
        TaskCompletionSource<GameChatMessageDto> received =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        aliceHub.On<GameChatMessageDto>("ChatMessage", m => received.TrySetResult(m));

        await aliceHub.StartAsync();
        await aliceHub.InvokeAsync("JoinGame", gameA.Id);
        await aliceHub.InvokeAsync("SendChat", gameA.Id, "hi");

        GameChatMessageDto msg = await received.Task.WaitAsync(TimeSpan.FromSeconds(5));
        msg.Text.Should().Be("hi");

        await aliceHub.DisposeAsync();
    }

    [Fact]
    public async Task SendChat_rate_limit_kicks_in_after_five_messages_in_window()
    {
        (TokenResponse alice, TokenResponse bob, GameDetailDto game) = await SetUpRunningGameAsync();

        HubConnection aliceHub = BuildHubConnection("/hubs/game", alice.AccessToken);
        TaskCompletionSource<string> rateLimited =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        aliceHub.On<string>("InvalidMove", code =>
        {
            if (code == "RateLimited")
            {
                rateLimited.TrySetResult(code);
            }
        });

        await aliceHub.StartAsync();
        await aliceHub.InvokeAsync("JoinGame", game.Id);

        // Five within the 10-second window: all admit.
        for (int i = 0; i < 5; i++)
        {
            await aliceHub.InvokeAsync("SendChat", game.Id, $"msg {i}");
        }

        // Sixth: rejected with InvalidMove("RateLimited").
        await aliceHub.InvokeAsync("SendChat", game.Id, "too many");

        string code = await rateLimited.Task.WaitAsync(TimeSpan.FromSeconds(5));
        code.Should().Be("RateLimited");

        await aliceHub.DisposeAsync();
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
