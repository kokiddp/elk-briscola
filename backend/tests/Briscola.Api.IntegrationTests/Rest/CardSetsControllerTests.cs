using System.Net;
using System.Net.Http.Json;
using Briscola.Api.Dtos;

namespace Briscola.Api.IntegrationTests.Rest;

/// <summary>
/// Wire-level pinning for <c>GET /api/v1/card-sets</c>. The catalog
/// reads <c>wwwroot/card-sets/*/manifest.json</c> at startup, so this
/// test exercises the on-disk fixtures shipped with the API image —
/// the placeholder, the piacentine slot, and the Napoletane set
/// sourced from Wikimedia Commons.
/// </summary>
public sealed class CardSetsControllerTests : RestTestBase
{
    [Fact]
    public async Task Returns_the_napoletane_set_with_jpg_assets()
    {
        using HttpClient client = Factory.CreateClient();
        CardSetManifestDto[] catalog =
            (await client.GetFromJsonAsync<CardSetManifestDto[]>(
                "/api/v1/card-sets", TestJsonOptions.Default))!;

        CardSetManifestDto? napoletane = catalog.FirstOrDefault(m => m.Id == "napoletane");
        napoletane.Should().NotBeNull(
            "the Napoletane card-set folder should be discovered at startup");

        napoletane!.Name.Should().Be("Napoletane");
        napoletane.FileExtension.Should().Be("jpg");
        napoletane.FilePattern.Should().Be("{suit}-{rank}.{ext}");
        napoletane.Preview.Should().Be("preview.jpg");
        napoletane.Back.Should().Be("back.jpg");
        napoletane.Path.Should().Be("/card-sets/napoletane/");
        // The licence string is free-form but should at least credit the
        // Commons source so the attribution-audit step has something to grep.
        napoletane.License.ToLowerInvariant().Should().Contain("commons");
    }

    [Fact]
    public async Task Napoletane_serves_each_of_the_40_card_assets_plus_back_plus_preview()
    {
        // Spot-check rather than fanning out 42 requests: one card per
        // suit + the back + the preview. Static-files middleware either
        // serves all of them or none — if a slug is wrong the 404 surfaces
        // here without needing the whole grid.
        using HttpClient client = Factory.CreateClient();
        string[] urls =
        [
            "/card-sets/napoletane/bastoni-asso.jpg",
            "/card-sets/napoletane/coppe-fante.jpg",
            "/card-sets/napoletane/denari-cavallo.jpg",
            "/card-sets/napoletane/spade-re.jpg",
            "/card-sets/napoletane/back.jpg",
            "/card-sets/napoletane/preview.jpg",
        ];
        foreach (string url in urls)
        {
            HttpResponseMessage r = await client.GetAsync(url);
            r.StatusCode.Should().Be(HttpStatusCode.OK, $"{url} should be served");
            r.Content.Headers.ContentType?.MediaType.Should().Be("image/jpeg");
            long? length = r.Content.Headers.ContentLength;
            length.Should().NotBeNull();
            length!.Value.Should().BeGreaterThan(1024, $"{url} should not be an empty placeholder");
        }
    }
}
