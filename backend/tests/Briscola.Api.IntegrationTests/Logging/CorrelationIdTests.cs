using System.Net;
using System.Net.Http.Headers;

namespace Briscola.Api.IntegrationTests.Logging;

/// <summary>
/// Verifies the server-side half of the correlation-id contract:
/// the response echoes the inbound <c>X-Correlation-Id</c> header, and
/// missing inbound headers are filled in with a server-generated value.
/// </summary>
public sealed class CorrelationIdTests : Rest.RestTestBase
{
    [Fact]
    public async Task Echoes_inbound_correlation_id_header()
    {
        using HttpClient client = Factory.CreateClient();
        const string Sent = "test-corr-abc-123";
        using HttpRequestMessage req = new(HttpMethod.Get, "/healthz");
        req.Headers.TryAddWithoutValidation("X-Correlation-Id", Sent);

        HttpResponseMessage res = await client.SendAsync(req);
        res.StatusCode.Should().Be(HttpStatusCode.OK);
        res.Headers.TryGetValues("X-Correlation-Id", out IEnumerable<string>? values)
            .Should().BeTrue();
        values!.Single().Should().Be(Sent);
    }

    [Fact]
    public async Task Generates_one_when_missing()
    {
        using HttpClient client = Factory.CreateClient();
        HttpResponseMessage res = await client.GetAsync("/healthz");
        res.StatusCode.Should().Be(HttpStatusCode.OK);
        res.Headers.TryGetValues("X-Correlation-Id", out IEnumerable<string>? values)
            .Should().BeTrue();
        string id = values!.Single();
        id.Should().NotBeNullOrWhiteSpace();
        id.Length.Should().BeGreaterThan(8);
    }

    [Fact]
    public async Task Drops_oversize_inbound_header()
    {
        using HttpClient client = Factory.CreateClient();
        string oversize = new('a', 256);
        using HttpRequestMessage req = new(HttpMethod.Get, "/healthz");
        req.Headers.TryAddWithoutValidation("X-Correlation-Id", oversize);

        HttpResponseMessage res = await client.SendAsync(req);
        res.Headers.TryGetValues("X-Correlation-Id", out IEnumerable<string>? values)
            .Should().BeTrue();
        // Oversize inbound (>128 chars) is replaced with a fresh server id —
        // an attacker can't poison log lines with arbitrary payload.
        values!.Single().Should().NotBe(oversize);
        values!.Single().Length.Should().BeLessThan(128);
    }
}
