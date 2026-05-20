using System.Net;
using System.Net.Http.Json;
using Briscola.Api.Dtos;

namespace Briscola.Api.IntegrationTests.Rest;

/// <summary>
/// Black-box coverage for <c>PATCH /api/v1/me</c>. The audit's M3
/// flagged that the endpoint accepted any <c>activeCardSetId</c>
/// string — a bad value could be persisted and then re-embedded as a
/// JWT claim on the next login.
/// </summary>
public sealed class MePatchTests : RestTestBase
{
    [Fact]
    public async Task Patch_with_known_active_card_set_id_succeeds()
    {
        using HttpClient client = Factory.CreateClient();
        await AuthFlowHelpers.RegisterAsync(client, "alice", "alice@e.com", "Strong-Pass-123");
        await AuthFlowHelpers.LoginAndAttachAsync(client, "alice", "Strong-Pass-123");

        HttpResponseMessage r = await client.PatchAsJsonAsync(
            "/api/v1/me",
            new MePatchRequest(DisplayName: null, ActiveCardSetId: "placeholder"));

        r.StatusCode.Should().Be(HttpStatusCode.OK);
        MeResponse me = (await r.Content.ReadFromJsonAsync<MeResponse>(TestJsonOptions.Default))!;
        me.ActiveCardSetId.Should().Be("placeholder");
    }

    [Fact]
    public async Task Patch_with_unknown_active_card_set_id_returns_400()
    {
        using HttpClient client = Factory.CreateClient();
        await AuthFlowHelpers.RegisterAsync(client, "bob", "bob@e.com", "Strong-Pass-123");
        await AuthFlowHelpers.LoginAndAttachAsync(client, "bob", "Strong-Pass-123");

        HttpResponseMessage r = await client.PatchAsJsonAsync(
            "/api/v1/me",
            new MePatchRequest(DisplayName: null, ActiveCardSetId: "does-not-exist"));

        r.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
