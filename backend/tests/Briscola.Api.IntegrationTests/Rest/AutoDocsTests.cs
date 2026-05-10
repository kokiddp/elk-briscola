using System.Net;
using Microsoft.Extensions.Hosting;

namespace Briscola.Api.IntegrationTests.Rest;

/// <summary>
/// Smoke tests for the auto-docs surface. The contract is "dev-only":
/// in <c>Development</c> the OpenAPI doc, Swagger UI, Scalar UI, and
/// the AsyncAPI sidecar all respond 200; in <c>Production</c> they're
/// absent (404 / no route). Public deployments stay surface-area-minimal.
/// </summary>
public sealed class AutoDocsTests : RestTestBase
{
    [Fact]
    public async Task OpenApi_doc_is_served_in_development()
    {
        Factory.Environment = Environments.Development;
        using HttpClient client = Factory.CreateClient();

        HttpResponseMessage res = await client.GetAsync("/swagger/v1/swagger.json");
        res.StatusCode.Should().Be(HttpStatusCode.OK);

        string body = await res.Content.ReadAsStringAsync();
        // Spot-check a few names that should always be in the spec.
        body.Should().Contain("\"openapi\":");
        body.Should().Contain("\"/api/v1/auth/login\"");
        body.Should().Contain("\"/api/v1/games\"");
        body.Should().Contain("\"Bearer\"", "the JWT security scheme is registered globally");
    }

    [Fact]
    public async Task Swagger_UI_is_served_in_development()
    {
        Factory.Environment = Environments.Development;
        using HttpClient client = Factory.CreateClient();

        HttpResponseMessage res = await client.GetAsync("/swagger/index.html");
        res.StatusCode.Should().Be(HttpStatusCode.OK);
        res.Content.Headers.ContentType?.MediaType.Should().Be("text/html");
    }

    [Fact]
    public async Task Scalar_UI_is_served_in_development()
    {
        Factory.Environment = Environments.Development;
        using HttpClient client = Factory.CreateClient();

        // Scalar mounts at /scalar (and serves the same UI from its
        // sub-routes too). Either responding 200 is fine.
        HttpResponseMessage res = await client.GetAsync("/scalar");
        res.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task AsyncApi_json_is_served_in_development()
    {
        Factory.Environment = Environments.Development;
        using HttpClient client = Factory.CreateClient();

        HttpResponseMessage res = await client.GetAsync("/docs/asyncapi.json");
        res.StatusCode.Should().Be(HttpStatusCode.OK);

        string body = await res.Content.ReadAsStringAsync();
        body.Should().Contain("\"asyncapi\":");
        body.Should().Contain("/hubs/lobby");
        body.Should().Contain("/hubs/game");
    }

    [Fact]
    public async Task AsyncApi_viewer_is_served_in_development()
    {
        Factory.Environment = Environments.Development;
        using HttpClient client = Factory.CreateClient();

        HttpResponseMessage res = await client.GetAsync("/docs/asyncapi");
        res.StatusCode.Should().Be(HttpStatusCode.OK);
        res.Content.Headers.ContentType?.MediaType.Should().Be("text/html");
    }

    [Fact]
    public async Task Docs_landing_lists_all_surfaces_in_development()
    {
        Factory.Environment = Environments.Development;
        using HttpClient client = Factory.CreateClient();

        HttpResponseMessage res = await client.GetAsync("/docs");
        res.StatusCode.Should().Be(HttpStatusCode.OK);
        string body = await res.Content.ReadAsStringAsync();
        body.Should().Contain("/scalar");
        body.Should().Contain("/swagger");
        body.Should().Contain("/docs/asyncapi");
    }

    [Fact]
    public async Task Docs_surface_is_absent_in_production()
    {
        // Default env is Production — the dev-only block in Program.cs
        // never runs, so every doc route should miss.
        using HttpClient client = Factory.CreateClient();

        foreach (string path in new[]
        {
            "/swagger/v1/swagger.json",
            "/swagger/index.html",
            "/scalar",
            "/docs",
            "/docs/asyncapi",
            "/docs/asyncapi.json",
        })
        {
            HttpResponseMessage res = await client.GetAsync(path);
            res.StatusCode.Should().Be(HttpStatusCode.NotFound, $"{path} must not be exposed in Production");
        }
    }
}
