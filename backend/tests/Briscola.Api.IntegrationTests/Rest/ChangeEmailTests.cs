using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Briscola.Api.Dtos;

namespace Briscola.Api.IntegrationTests.Rest;

/// <summary>
/// Wire-level coverage for <c>POST /api/v1/auth/change-email</c>. Pins
/// the current-password confirmation, the email-already-taken 409,
/// the same-email no-op success, and the security-stamp bump that
/// invalidates outstanding access tokens.
/// </summary>
public sealed class ChangeEmailTests : RestTestBase
{
    [Fact]
    public async Task Returns_204_and_persists_the_new_email_on_success()
    {
        using HttpClient client = Factory.CreateClient();
        await AuthFlowHelpers.RegisterAsync(client, "alice", "alice@example.com", "Strong-Pass-123");
        await AuthFlowHelpers.LoginAndAttachAsync(client, "alice", "Strong-Pass-123");

        HttpResponseMessage r = await client.PostAsJsonAsync(
            "/api/v1/auth/change-email",
            new ChangeEmailRequest(CurrentPassword: "Strong-Pass-123", NewEmail: "alice2@example.com"));
        r.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Login with the new email should now work.
        HttpResponseMessage login = await client.PostAsJsonAsync(
            "/api/v1/auth/login",
            new LoginRequest("alice2@example.com", "Strong-Pass-123"));
        login.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Returns_400_when_current_password_is_wrong()
    {
        using HttpClient client = Factory.CreateClient();
        await AuthFlowHelpers.RegisterAsync(client, "bob", "bob@example.com", "Strong-Pass-123");
        await AuthFlowHelpers.LoginAndAttachAsync(client, "bob", "Strong-Pass-123");

        HttpResponseMessage r = await client.PostAsJsonAsync(
            "/api/v1/auth/change-email",
            new ChangeEmailRequest(CurrentPassword: "WRONG", NewEmail: "bob2@example.com"));
        r.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Returns_409_when_target_email_is_already_taken()
    {
        using HttpClient client = Factory.CreateClient();
        await AuthFlowHelpers.RegisterAsync(client, "carol", "carol@example.com", "Strong-Pass-123");
        await AuthFlowHelpers.RegisterAsync(client, "dan", "dan@example.com", "Strong-Pass-123");
        await AuthFlowHelpers.LoginAndAttachAsync(client, "carol", "Strong-Pass-123");

        HttpResponseMessage r = await client.PostAsJsonAsync(
            "/api/v1/auth/change-email",
            new ChangeEmailRequest(CurrentPassword: "Strong-Pass-123", NewEmail: "dan@example.com"));
        r.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Setting_the_same_email_is_an_idempotent_no_op_success()
    {
        using HttpClient client = Factory.CreateClient();
        await AuthFlowHelpers.RegisterAsync(client, "eve", "eve@example.com", "Strong-Pass-123");
        await AuthFlowHelpers.LoginAndAttachAsync(client, "eve", "Strong-Pass-123");

        HttpResponseMessage r = await client.PostAsJsonAsync(
            "/api/v1/auth/change-email",
            new ChangeEmailRequest(CurrentPassword: "Strong-Pass-123", NewEmail: "eve@example.com"));
        r.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Invalidates_outstanding_access_tokens_on_success()
    {
        using HttpClient client = Factory.CreateClient();
        await AuthFlowHelpers.RegisterAsync(client, "frank", "frank@example.com", "Strong-Pass-123");
        await AuthFlowHelpers.LoginAndAttachAsync(client, "frank", "Strong-Pass-123");

        // Confirm the token works against /me first.
        HttpResponseMessage meBefore = await client.GetAsync("/api/v1/me");
        meBefore.StatusCode.Should().Be(HttpStatusCode.OK);

        HttpResponseMessage change = await client.PostAsJsonAsync(
            "/api/v1/auth/change-email",
            new ChangeEmailRequest(CurrentPassword: "Strong-Pass-123", NewEmail: "frank2@example.com"));
        change.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // The old access token's SecurityStamp claim no longer matches
        // the user's current stamp — SecurityStampValidator rejects it.
        HttpResponseMessage meAfter = await client.GetAsync("/api/v1/me");
        meAfter.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
