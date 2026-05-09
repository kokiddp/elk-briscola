using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Briscola.Api.Dtos;

namespace Briscola.Api.IntegrationTests.Rest;

public sealed class AuthFlowTests : RestTestBase
{
    [Fact]
    public async Task Register_then_login_then_me_round_trips()
    {
        using HttpClient client = Factory.CreateClient();

        HttpResponseMessage register = await client.PostAsJsonAsync("/api/v1/auth/register", new RegisterRequest(
            Username: "alice",
            Email: "alice@example.com",
            Password: "Strong-Pass-123",
            DisplayName: "Alice"));
        register.StatusCode.Should().Be(HttpStatusCode.Created);

        TokenResponse tokens = await LoginAsync(client, "alice", "Strong-Pass-123");

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
        MeResponse me = (await client.GetFromJsonAsync<MeResponse>("/api/v1/me", TestJsonOptions.Default))!;
        me.Username.Should().Be("alice");
        me.DisplayName.Should().Be("Alice");
        me.Ranking.Elo.Should().Be(1500);
    }

    [Fact]
    public async Task Refresh_rotates_tokens_and_replayed_refresh_is_rejected()
    {
        using HttpClient client = Factory.CreateClient();
        await RegisterAsync(client, "bob", "bob@example.com", "Strong-Pass-123");

        TokenResponse first = await LoginAsync(client, "bob", "Strong-Pass-123");

        HttpResponseMessage rotated = await client.PostAsJsonAsync("/api/v1/auth/refresh",
            new RefreshRequest(first.RefreshToken));
        rotated.StatusCode.Should().Be(HttpStatusCode.OK);
        TokenResponse second = (await rotated.Content.ReadFromJsonAsync<TokenResponse>(TestJsonOptions.Default))!;
        second.RefreshToken.Should().NotBe(first.RefreshToken);

        // Replaying the original refresh token revokes the chain (defense
        // against token theft) and returns 401.
        HttpResponseMessage replay = await client.PostAsJsonAsync("/api/v1/auth/refresh",
            new RefreshRequest(first.RefreshToken));
        replay.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Change_password_invalidates_outstanding_access_tokens()
    {
        using HttpClient client = Factory.CreateClient();
        await RegisterAsync(client, "carol", "carol@example.com", "Strong-Pass-123");
        TokenResponse tokens = await LoginAsync(client, "carol", "Strong-Pass-123");

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);

        HttpResponseMessage change = await client.PostAsJsonAsync("/api/v1/auth/change-password",
            new ChangePasswordRequest("Strong-Pass-123", "Stronger-Pass-456"));
        change.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // The same access token now hits a stale SecurityStamp and is rejected.
        HttpResponseMessage me = await client.GetAsync("/api/v1/me");
        me.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        // Logging in with the new password works.
        TokenResponse fresh = await LoginAsync(client, "carol", "Stronger-Pass-456");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", fresh.AccessToken);
        HttpResponseMessage meAgain = await client.GetAsync("/api/v1/me");
        meAgain.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Logout_revokes_refresh_token()
    {
        using HttpClient client = Factory.CreateClient();
        await RegisterAsync(client, "dave", "dave@example.com", "Strong-Pass-123");
        TokenResponse tokens = await LoginAsync(client, "dave", "Strong-Pass-123");

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
        HttpResponseMessage logout = await client.PostAsJsonAsync("/api/v1/auth/logout",
            new LogoutRequest(tokens.RefreshToken));
        logout.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // The refresh token is now revoked: rotation fails.
        HttpResponseMessage replay = await client.PostAsJsonAsync("/api/v1/auth/refresh",
            new RefreshRequest(tokens.RefreshToken));
        replay.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Wrong_password_returns_401()
    {
        using HttpClient client = Factory.CreateClient();
        await RegisterAsync(client, "erin", "erin@example.com", "Strong-Pass-123");

        HttpResponseMessage login = await client.PostAsJsonAsync("/api/v1/auth/login",
            new LoginRequest("erin", "WrongPassword-1"));
        login.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    private static async Task RegisterAsync(HttpClient client, string username, string email, string password)
    {
        HttpResponseMessage r = await client.PostAsJsonAsync("/api/v1/auth/register",
            new RegisterRequest(username, email, password, username));
        r.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    private static async Task<TokenResponse> LoginAsync(HttpClient client, string username, string password)
    {
        HttpResponseMessage r = await client.PostAsJsonAsync("/api/v1/auth/login",
            new LoginRequest(username, password));
        r.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await r.Content.ReadFromJsonAsync<TokenResponse>(TestJsonOptions.Default))!;
    }
}
