using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Briscola.Api.Dtos;
using Briscola.Domain.Primitives;

namespace Briscola.Api.IntegrationTests.Rest;

public sealed class LobbyControllerTests : RestTestBase
{
    [Fact]
    public async Task Create_then_list_then_join_auto_starts_two_player_game()
    {
        using HttpClient alice = Factory.CreateClient();
        using HttpClient bob = Factory.CreateClient();

        await AuthFlowHelpers.RegisterAsync(alice, "alice", "alice@example.com", "Strong-Pass-123");
        await AuthFlowHelpers.RegisterAsync(bob, "bob", "bob@example.com", "Strong-Pass-123");

        await AuthFlowHelpers.LoginAndAttachAsync(alice, "alice", "Strong-Pass-123");
        await AuthFlowHelpers.LoginAndAttachAsync(bob, "bob", "Strong-Pass-123");

        // Alice creates an open 2p game.
        HttpResponseMessage create = await alice.PostAsJsonAsync("/api/v1/games", new CreateGameRequestDto(
            Mode: GameMode.TwoPlayer,
            Name: "Alice's table",
            IsPrivate: false,
            Password: null));
        create.StatusCode.Should().Be(HttpStatusCode.Created);
        GameDetailDto created = (await create.Content.ReadFromJsonAsync<GameDetailDto>(TestJsonOptions.Default))!;
        created.Status.Should().Be(GameStatus.Open);
        created.Seats.Should().HaveCount(2);

        // Listing open games surfaces it.
        IEnumerable<GameSummaryDto>? listed =
            await bob.GetFromJsonAsync<IEnumerable<GameSummaryDto>>(
                $"/api/v1/games?status={GameStatus.Open}", TestJsonOptions.Default);
        listed.Should().Contain(g => g.Id == created.Id);

        // Bob joins → both seats full → auto-start.
        HttpResponseMessage join = await bob.PostAsJsonAsync($"/api/v1/games/{created.Id}/join",
            new JoinGameRequestDto(null));
        join.StatusCode.Should().Be(HttpStatusCode.OK);
        GameDetailDto running = (await join.Content.ReadFromJsonAsync<GameDetailDto>(TestJsonOptions.Default))!;
        running.Status.Should().Be(GameStatus.Running);
        running.StartedAt.Should().NotBeNull();
        running.Seats.Should().AllSatisfy(id => id.HasValue.Should().BeTrue());
    }

    [Fact]
    public async Task Cannot_leave_running_game()
    {
        using HttpClient alice = Factory.CreateClient();
        using HttpClient bob = Factory.CreateClient();
        await AuthFlowHelpers.RegisterAsync(alice, "alice", "alice@example.com", "Strong-Pass-123");
        await AuthFlowHelpers.RegisterAsync(bob, "bob", "bob@example.com", "Strong-Pass-123");
        await AuthFlowHelpers.LoginAndAttachAsync(alice, "alice", "Strong-Pass-123");
        await AuthFlowHelpers.LoginAndAttachAsync(bob, "bob", "Strong-Pass-123");

        HttpResponseMessage create = await alice.PostAsJsonAsync("/api/v1/games",
            new CreateGameRequestDto(GameMode.TwoPlayer, "table", false, null));
        Guid id = (await create.Content.ReadFromJsonAsync<GameDetailDto>(TestJsonOptions.Default))!.Id;
        await bob.PostAsJsonAsync($"/api/v1/games/{id}/join", new JoinGameRequestDto(null));

        HttpResponseMessage leave = await alice.PostAsync($"/api/v1/games/{id}/leave", content: null);
        leave.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Joining_a_started_game_returns_409()
    {
        using HttpClient alice = Factory.CreateClient();
        using HttpClient bob = Factory.CreateClient();
        using HttpClient carol = Factory.CreateClient();
        await AuthFlowHelpers.RegisterAsync(alice, "alice", "alice@example.com", "Strong-Pass-123");
        await AuthFlowHelpers.RegisterAsync(bob, "bob", "bob@example.com", "Strong-Pass-123");
        await AuthFlowHelpers.RegisterAsync(carol, "carol", "carol@example.com", "Strong-Pass-123");
        await AuthFlowHelpers.LoginAndAttachAsync(alice, "alice", "Strong-Pass-123");
        await AuthFlowHelpers.LoginAndAttachAsync(bob, "bob", "Strong-Pass-123");
        await AuthFlowHelpers.LoginAndAttachAsync(carol, "carol", "Strong-Pass-123");

        HttpResponseMessage create = await alice.PostAsJsonAsync("/api/v1/games",
            new CreateGameRequestDto(GameMode.TwoPlayer, "table", false, null));
        Guid id = (await create.Content.ReadFromJsonAsync<GameDetailDto>(TestJsonOptions.Default))!.Id;
        await bob.PostAsJsonAsync($"/api/v1/games/{id}/join", new JoinGameRequestDto(null));

        HttpResponseMessage thirdJoin = await carol.PostAsJsonAsync($"/api/v1/games/{id}/join",
            new JoinGameRequestDto(null));
        thirdJoin.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }
}

internal static class AuthFlowHelpers
{
    public static async Task RegisterAsync(HttpClient client, string username, string email, string password)
    {
        HttpResponseMessage r = await client.PostAsJsonAsync("/api/v1/auth/register",
            new RegisterRequest(username, email, password, username));
        r.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    public static async Task<TokenResponse> LoginAndAttachAsync(HttpClient client, string username, string password)
    {
        HttpResponseMessage r = await client.PostAsJsonAsync("/api/v1/auth/login",
            new LoginRequest(username, password));
        r.StatusCode.Should().Be(HttpStatusCode.OK);
        TokenResponse tokens = (await r.Content.ReadFromJsonAsync<TokenResponse>(TestJsonOptions.Default))!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
        return tokens;
    }
}
