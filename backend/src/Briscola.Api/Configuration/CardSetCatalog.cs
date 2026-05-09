using System.Collections.Immutable;
using System.Text.Json;
using Briscola.Api.Dtos;

namespace Briscola.Api.Configuration;

file static class JsonOptionsCache
{
    public static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);
}

/// <summary>
/// Loads <c>wwwroot/card-sets/&lt;id&gt;/manifest.json</c> at startup and
/// caches the parsed manifests in-memory for fast <c>GET /card-sets</c>
/// responses. Phase 9 will populate the directory; until then the catalog
/// is empty (anonymous endpoint returns <c>[]</c>).
/// </summary>
public sealed class CardSetCatalog
{
    private readonly ImmutableArray<CardSetManifestDto> _items;

    public CardSetCatalog(ImmutableArray<CardSetManifestDto> items) => _items = items;

    public ImmutableArray<CardSetManifestDto> All => _items;

    public static CardSetCatalog LoadFromWebRoot(IWebHostEnvironment env)
    {
        ArgumentNullException.ThrowIfNull(env);
        string webRoot = env.WebRootPath ?? Path.Combine(env.ContentRootPath, "wwwroot");
        string root = Path.Combine(webRoot, "card-sets");
        if (!Directory.Exists(root))
        {
            return new CardSetCatalog(ImmutableArray<CardSetManifestDto>.Empty);
        }

        List<CardSetManifestDto> items = [];
        foreach (string dir in Directory.EnumerateDirectories(root))
        {
            string manifest = Path.Combine(dir, "manifest.json");
            if (!File.Exists(manifest))
            {
                continue;
            }

            using FileStream stream = File.OpenRead(manifest);
            CardSetManifestDto? parsed = JsonSerializer.Deserialize<CardSetManifestDto>(stream, JsonOptionsCache.Web);
            if (parsed is not null)
            {
                items.Add(parsed with { Path = $"/card-sets/{Path.GetFileName(dir)}/" });
            }
        }

        return new CardSetCatalog([.. items]);
    }
}
