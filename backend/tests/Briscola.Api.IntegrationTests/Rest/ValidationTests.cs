using System.Net;
using System.Net.Http.Json;
using Briscola.Api.Dtos;

namespace Briscola.Api.IntegrationTests.Rest;

public sealed class ValidationTests : RestTestBase
{
    [Fact]
    public async Task Short_password_returns_422_with_validation_problem_details()
    {
        using HttpClient client = Factory.CreateClient();
        HttpResponseMessage r = await client.PostAsJsonAsync("/api/v1/auth/register",
            new RegisterRequest("alice", "alice@example.com", "short1", null));

        r.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        r.Content.Headers.ContentType?.MediaType.Should().StartWith("application/");
    }

    [Fact]
    public async Task Invalid_username_returns_422()
    {
        using HttpClient client = Factory.CreateClient();
        HttpResponseMessage r = await client.PostAsJsonAsync("/api/v1/auth/register",
            new RegisterRequest("ab", "alice@example.com", "Strong-Pass-123", null));

        r.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }
}
