using System.Net;
using System.Net.Http.Json;
using Briscola.Api.Dtos;

namespace Briscola.Api.IntegrationTests.Rest;

public sealed class RateLimitTests : RestTestBase
{
    [Fact]
    public async Task Sixth_login_in_a_minute_returns_429()
    {
        using HttpClient client = Factory.CreateClient();
        await AuthFlowHelpers.RegisterAsync(client, "alice", "alice@example.com", "Strong-Pass-123");

        // 5 wrong-password attempts: each returns 401 but consumes a slot.
        for (int i = 0; i < 5; i++)
        {
            HttpResponseMessage r = await client.PostAsJsonAsync("/api/v1/auth/login",
                new LoginRequest("alice", "Bad-Pass-99"));
            r.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }

        // 6th hits the limiter.
        HttpResponseMessage limited = await client.PostAsJsonAsync("/api/v1/auth/login",
            new LoginRequest("alice", "Bad-Pass-99"));
        limited.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task Sixth_change_password_attempt_returns_429()
    {
        // Tight policy so the test runs in milliseconds without burning
        // the production 5-per-15-min budget. The point is that the
        // policy is *applied* to the endpoint at all (H1's pre-fix
        // state was: no policy → uncapped brute force).
        Factory.ExtraSettings["RateLimits:auth-change-password:PermitLimit"] = "5";
        Factory.ExtraSettings["RateLimits:auth-change-password:WindowSeconds"] = "60";

        using HttpClient client = Factory.CreateClient();
        await AuthFlowHelpers.RegisterAsync(client, "alice", "alice@example.com", "Strong-Pass-123");
        await AuthFlowHelpers.LoginAndAttachAsync(client, "alice", "Strong-Pass-123");

        // Five wrong-current-password attempts — each returns 400 but
        // consumes a slot.
        for (int i = 0; i < 5; i++)
        {
            HttpResponseMessage r = await client.PostAsJsonAsync(
                "/api/v1/auth/change-password",
                new ChangePasswordRequest("Bad-Pass-99", "New-Strong-Pass-456"));
            r.StatusCode.Should().BeOneOf(HttpStatusCode.BadRequest, HttpStatusCode.Unauthorized);
        }

        HttpResponseMessage limited = await client.PostAsJsonAsync(
            "/api/v1/auth/change-password",
            new ChangePasswordRequest("Bad-Pass-99", "New-Strong-Pass-456"));
        limited.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task Logout_endpoint_is_rate_limited_per_user()
    {
        Factory.ExtraSettings["RateLimits:auth-logout:PermitLimit"] = "3";
        Factory.ExtraSettings["RateLimits:auth-logout:WindowSeconds"] = "60";

        using HttpClient client = Factory.CreateClient();
        await AuthFlowHelpers.RegisterAsync(client, "alice", "alice@example.com", "Strong-Pass-123");
        await AuthFlowHelpers.LoginAndAttachAsync(client, "alice", "Strong-Pass-123");

        for (int i = 0; i < 3; i++)
        {
            HttpResponseMessage r = await client.PostAsJsonAsync(
                "/api/v1/auth/logout",
                new LogoutRequest("some-unknown-refresh-token"));
            r.StatusCode.Should().Be(HttpStatusCode.NoContent);
        }

        HttpResponseMessage limited = await client.PostAsJsonAsync(
            "/api/v1/auth/logout",
            new LogoutRequest("some-unknown-refresh-token"));
        limited.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
    }
}
