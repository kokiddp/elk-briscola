using System.Net;
using System.Net.Http.Json;
using Briscola.Api.Dtos;
using Briscola.Domain.Primitives;

namespace Briscola.Api.IntegrationTests.Rest;

public sealed class PrivateGameTests : RestTestBase
{
    [Fact]
    public async Task Wrong_password_rejected_correct_accepted()
    {
        using HttpClient alice = Factory.CreateClient();
        using HttpClient bob = Factory.CreateClient();

        await AuthFlowHelpers.RegisterAsync(alice, "alice", "alice@example.com", "Strong-Pass-123");
        await AuthFlowHelpers.RegisterAsync(bob, "bob", "bob@example.com", "Strong-Pass-123");
        await AuthFlowHelpers.LoginAndAttachAsync(alice, "alice", "Strong-Pass-123");
        await AuthFlowHelpers.LoginAndAttachAsync(bob, "bob", "Strong-Pass-123");

        const string secret = "let-me-in";
        HttpResponseMessage create = await alice.PostAsJsonAsync("/api/v1/games",
            new CreateGameRequestDto(GameMode.TwoPlayer, "private", IsPrivate: true, Password: secret));
        Guid id = (await create.Content.ReadFromJsonAsync<GameDetailDto>(TestJsonOptions.Default))!.Id;

        HttpResponseMessage wrong = await bob.PostAsJsonAsync($"/api/v1/games/{id}/join",
            new JoinGameRequestDto("not-the-password"));
        wrong.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        HttpResponseMessage right = await bob.PostAsJsonAsync($"/api/v1/games/{id}/join",
            new JoinGameRequestDto(secret));
        right.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
