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
}
