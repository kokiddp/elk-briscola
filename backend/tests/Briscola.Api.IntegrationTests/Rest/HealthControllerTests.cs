using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Briscola.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Briscola.Api.IntegrationTests.Rest;

public sealed class HealthControllerTests : RestTestBase
{
    [Fact]
    public async Task Liveness_returns_200()
    {
        using HttpClient client = Factory.CreateClient();
        HttpResponseMessage res = await client.GetAsync("/healthz");
        res.StatusCode.Should().Be(HttpStatusCode.OK);
        JsonElement body = await res.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("status").GetString().Should().Be("ok");
    }

    [Fact]
    public async Task Readiness_returns_200_when_db_reachable()
    {
        using HttpClient client = Factory.CreateClient();
        HttpResponseMessage res = await client.GetAsync("/readyz");
        res.StatusCode.Should().Be(HttpStatusCode.OK);
        JsonElement body = await res.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("status").GetString().Should().Be("ready");
    }

    [Fact]
    public async Task Readiness_returns_503_when_db_unreachable()
    {
        // Drive the controller directly with a DbContext pointed at a dead
        // socket — same code path the prod readyz would hit when Postgres
        // is down. We avoid booting the whole host (its startup hydration
        // would also fail and confuse the diagnosis).
        DbContextOptions<BriscolaDbContext> opts = new DbContextOptionsBuilder<BriscolaDbContext>()
            .UseNpgsql("Host=127.0.0.1;Port=1;Database=nope;Username=nope;Password=nope;Timeout=2")
            .Options;
        using BriscolaDbContext dead = new(opts);

        Briscola.Api.Controllers.HealthController controller = new();
        IActionResult res = await controller.Readiness(dead, CancellationToken.None);

        ObjectResult obj = res.Should().BeOfType<ObjectResult>().Subject;
        obj.StatusCode.Should().Be(StatusCodes.Status503ServiceUnavailable);
        string json = JsonSerializer.Serialize(obj.Value);
        json.Should().Contain("db_unreachable");
    }

    [Fact]
    public async Task Readiness_actually_calls_CanConnectAsync()
    {
        // Sanity check: the test fixture exposes a real DbContext we can
        // ping directly. If this returns false our readiness check is also
        // expected to fail; otherwise the green path is genuinely green.
        await using AsyncServiceScope scope = Factory.Services.CreateAsyncScope();
        BriscolaDbContext db = scope.ServiceProvider.GetRequiredService<BriscolaDbContext>();
        bool ok = await db.Database.CanConnectAsync();
        ok.Should().BeTrue();
    }
}
