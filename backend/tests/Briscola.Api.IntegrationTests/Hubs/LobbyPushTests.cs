using System.Net.Http.Headers;
using System.Net.Http.Json;
using Briscola.Api.Dtos;
using Briscola.Api.IntegrationTests.Rest;
using Briscola.Domain.Primitives;
using Microsoft.AspNetCore.SignalR.Client;

namespace Briscola.Api.IntegrationTests.Hubs;

/// <summary>
/// End-to-end coverage of the LobbyHub push surface (gameCreated / Updated
/// / Started / Ended). These pushes are emitted by `GameEventDispatcher`
/// in response to bus events published by `LobbyService` + `GameRoom`.
/// Without this test, regressions to the dispatcher routing went unnoticed
/// for an entire phase (the live browser flow caught it in Phase 9).
/// </summary>
[Collection(HubTests.Name)]
public sealed class LobbyPushTests : HubTestHarness
{
    [Fact]
    public async Task gameCreated_reaches_a_subscribed_lobby_observer()
    {
        TokenResponse observer = await RegisterAndLoginAsync("observer");
        TokenResponse alice = await RegisterAndLoginAsync("alice");

        HubConnection lobbyHub = BuildHubConnection("/hubs/lobby", observer.AccessToken);
        TaskCompletionSource<GameSummaryDto> created =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        lobbyHub.On<GameSummaryDto>("GameCreated", s => created.TrySetResult(s));
        await lobbyHub.StartAsync();
        await lobbyHub.InvokeAsync("SubscribeOpen");

        using HttpClient aliceHttp = NewClient(alice.AccessToken);
        await aliceHttp.PostAsJsonAsync(
            "/api/v1/games",
            new CreateGameRequestDto(GameMode.TwoPlayer, "lobby push", false, null));

        GameSummaryDto summary = await created.Task.WaitAsync(TimeSpan.FromSeconds(5));
        summary.Name.Should().Be("lobby push");
        summary.Status.Should().Be(GameStatus.Open);
        summary.OccupiedSeats.Should().Be(1);

        await lobbyHub.DisposeAsync();
    }

    [Fact]
    public async Task gameUpdated_and_gameStarted_fire_on_the_seat_filling_join()
    {
        TokenResponse observer = await RegisterAndLoginAsync("observer");
        TokenResponse alice = await RegisterAndLoginAsync("alice");
        TokenResponse bob = await RegisterAndLoginAsync("bob");

        HubConnection lobbyHub = BuildHubConnection("/hubs/lobby", observer.AccessToken);
        TaskCompletionSource<GameSummaryDto> created =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource<GameSummaryDto> updated =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource<Guid> started =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        lobbyHub.On<GameSummaryDto>("GameCreated", s => created.TrySetResult(s));
        lobbyHub.On<GameSummaryDto>("GameUpdated", s => updated.TrySetResult(s));
        lobbyHub.On<Guid>("GameStarted", id => started.TrySetResult(id));
        await lobbyHub.StartAsync();
        await lobbyHub.InvokeAsync("SubscribeOpen");

        using HttpClient aliceHttp = NewClient(alice.AccessToken);
        using HttpClient bobHttp = NewClient(bob.AccessToken);

        HttpResponseMessage createRes = await aliceHttp.PostAsJsonAsync(
            "/api/v1/games",
            new CreateGameRequestDto(GameMode.TwoPlayer, "fill-me", false, null));
        GameDetailDto open = (await createRes.Content.ReadFromJsonAsync<GameDetailDto>(TestJsonOptions.Default))!;
        await created.Task.WaitAsync(TimeSpan.FromSeconds(5));

        await bobHttp.PostAsJsonAsync($"/api/v1/games/{open.Id}/join", new JoinGameRequestDto(null));

        GameSummaryDto upd = await updated.Task.WaitAsync(TimeSpan.FromSeconds(5));
        upd.Status.Should().Be(GameStatus.Running);
        upd.OccupiedSeats.Should().Be(2);

        Guid startedId = await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        startedId.Should().Be(open.Id);

        await lobbyHub.DisposeAsync();
    }

    [Fact]
    public async Task gameEnded_fires_when_the_last_player_leaves_an_open_game()
    {
        TokenResponse observer = await RegisterAndLoginAsync("observer");
        TokenResponse alice = await RegisterAndLoginAsync("alice");

        HubConnection lobbyHub = BuildHubConnection("/hubs/lobby", observer.AccessToken);
        TaskCompletionSource<GameSummaryDto> created =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource<Guid> ended =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        lobbyHub.On<GameSummaryDto>("GameCreated", s => created.TrySetResult(s));
        lobbyHub.On<Guid>("GameEnded", id => ended.TrySetResult(id));
        await lobbyHub.StartAsync();
        await lobbyHub.InvokeAsync("SubscribeOpen");

        using HttpClient aliceHttp = NewClient(alice.AccessToken);
        HttpResponseMessage createRes = await aliceHttp.PostAsJsonAsync(
            "/api/v1/games",
            new CreateGameRequestDto(GameMode.TwoPlayer, "leaver", false, null));
        GameDetailDto open = (await createRes.Content.ReadFromJsonAsync<GameDetailDto>(TestJsonOptions.Default))!;
        await created.Task.WaitAsync(TimeSpan.FromSeconds(5));

        await aliceHttp.PostAsync($"/api/v1/games/{open.Id}/leave", content: null);

        // The lone creator leaving drains the table — the lobby gets a
        // GameEnded push (not GameUpdated) so the open list drops the
        // row immediately instead of showing a 0-of-2 ghost record.
        Guid endedId = await ended.Task.WaitAsync(TimeSpan.FromSeconds(5));
        endedId.Should().Be(open.Id);

        await lobbyHub.DisposeAsync();
    }

    private HttpClient NewClient(string accessToken)
    {
        HttpClient client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return client;
    }
}
