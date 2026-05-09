namespace Briscola.Api.IntegrationTests.Rest;

public sealed class SecurityHeadersTests : RestTestBase
{
    [Fact]
    public async Task Healthz_response_carries_the_documented_security_headers()
    {
        using HttpClient client = Factory.CreateClient();
        HttpResponseMessage response = await client.GetAsync("/healthz");

        response.Headers.Should().ContainKey("X-Content-Type-Options")
            .WhoseValue.Should().Contain("nosniff");
        response.Headers.Should().ContainKey("Referrer-Policy")
            .WhoseValue.Should().Contain("strict-origin-when-cross-origin");
        response.Headers.Should().ContainKey("Permissions-Policy");
        response.Headers.Should().ContainKey("Content-Security-Policy");
        response.Headers.Should().ContainKey("Strict-Transport-Security");
    }
}
