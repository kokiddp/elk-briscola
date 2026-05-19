using System.Text;
using Briscola.Api.CardSets;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Xunit;

namespace Briscola.Api.IntegrationTests.Unit;

/// <summary>
/// Filesystem-only unit tests for <see cref="CardSetCatalog"/>. No Postgres
/// or Testcontainers — these spin up a temp <c>wwwroot/card-sets/</c>
/// hierarchy on disk and assert what the catalog discovers.
/// </summary>
public sealed class CardSetCatalogTests : IDisposable
{
    private readonly string _root;
    private readonly FakeEnv _env;

    public CardSetCatalogTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "elk-cardsets-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _env = new FakeEnv(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch { /* best-effort cleanup */ }
    }

    [Fact]
    public void Returns_empty_catalog_when_wwwroot_card_sets_is_absent()
    {
        CardSetCatalog catalog = CardSetCatalog.LoadFromWebRoot(_env);
        catalog.All.Should().BeEmpty();
        catalog.Contains("placeholder").Should().BeFalse();
    }

    [Fact]
    public void Loads_well_formed_manifests_with_server_derived_path()
    {
        WriteManifest("placeholder", new
        {
            id = "placeholder",
            name = "Placeholder",
            license = "Bundled",
            preview = "preview.svg",
            fileExtension = "svg",
            filePattern = "{suit}-{rank}.{ext}",
            back = "back.svg",
        });

        CardSetCatalog catalog = CardSetCatalog.LoadFromWebRoot(_env);
        catalog.All.Should().HaveCount(1);
        catalog.All[0].Id.Should().Be("placeholder");
        catalog.All[0].Path.Should().Be("/card-sets/placeholder/");
        catalog.Contains("placeholder").Should().BeTrue();
        catalog.Contains("does-not-exist").Should().BeFalse();
    }

    [Fact]
    public void Skips_malformed_manifests_without_throwing()
    {
        Directory.CreateDirectory(Path.Combine(_root, "card-sets", "broken"));
        File.WriteAllText(
            Path.Combine(_root, "card-sets", "broken", "manifest.json"),
            "{ this is not json",
            Encoding.UTF8);

        WriteManifest("good", new
        {
            id = "good",
            name = "Good",
            license = "Bundled",
            preview = "preview.svg",
            fileExtension = "svg",
            filePattern = "{suit}-{rank}.{ext}",
            back = "back.svg",
        });

        CardSetCatalog catalog = CardSetCatalog.LoadFromWebRoot(_env);
        catalog.All.Should().HaveCount(1);
        catalog.All[0].Id.Should().Be("good");
    }

    [Theory]
    [InlineData("svg")]
    [InlineData("png")]
    [InlineData("jpg")]
    [InlineData("jpeg")]
    [InlineData("webp")]
    [InlineData("avif")]
    public void Loads_manifests_with_any_supported_raster_or_vector_extension(string ext)
    {
        // The static-files middleware ships a Content-Type for each of
        // these; the catalog accepts them without a warning. Verifies
        // the supported-extensions table is in sync with what the
        // browser + middleware understand.
        WriteManifest("custom", new
        {
            id = "custom",
            name = "Custom",
            license = "Bundled",
            preview = $"preview.{ext}",
            fileExtension = ext,
            filePattern = "{suit}-{rank}.{ext}",
            back = $"back.{ext}",
        });

        CardSetCatalog catalog = CardSetCatalog.LoadFromWebRoot(_env);
        catalog.All.Should().HaveCount(1);
        catalog.All[0].FileExtension.Should().Be(ext);
    }

    [Fact]
    public void Port_surface_exposes_default_id_and_iteration_order()
    {
        WriteManifest("placeholder", BasicManifest("placeholder"));
        WriteManifest("piacentine", BasicManifest("piacentine"));

        CardSetCatalog catalog = CardSetCatalog.LoadFromWebRoot(_env);
        Briscola.Application.Ports.ICardSetCatalog port = catalog;

        port.DefaultSetId.Should().Be("placeholder");
        port.AllIds.Should().Contain("placeholder");
        port.AllIds.Should().Contain("piacentine");
    }

    private void WriteManifest(string setId, object body)
    {
        string dir = Path.Combine(_root, "card-sets", setId);
        Directory.CreateDirectory(dir);
        File.WriteAllText(
            Path.Combine(dir, "manifest.json"),
            System.Text.Json.JsonSerializer.Serialize(body),
            Encoding.UTF8);
    }

    private static object BasicManifest(string id) => new
    {
        id,
        name = id,
        license = "Bundled",
        preview = "preview.svg",
        fileExtension = "svg",
        filePattern = "{suit}-{rank}.{ext}",
        back = "back.svg",
    };

    private sealed class FakeEnv : IWebHostEnvironment
    {
        public FakeEnv(string root)
        {
            WebRootPath = root;
            ContentRootPath = root;
            EnvironmentName = "Test";
            ApplicationName = "elk-briscola-test";
            WebRootFileProvider = null!;
            ContentRootFileProvider = null!;
        }

        public string WebRootPath { get; set; }
        public Microsoft.Extensions.FileProviders.IFileProvider WebRootFileProvider { get; set; }
        public string ApplicationName { get; set; }
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; }
        public string ContentRootPath { get; set; }
        public string EnvironmentName { get; set; }
    }
}
