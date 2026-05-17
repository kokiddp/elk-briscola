using System.Collections.Immutable;
using System.Text.Json;
using Briscola.Api.Dtos;
using Briscola.Application.Ports;

namespace Briscola.Api.CardSets;

file static class JsonOptionsCache
{
    public static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);
}

/// <summary>
/// Scans <c>wwwroot/card-sets/&lt;id&gt;/manifest.json</c> at startup and
/// caches the parsed manifests in-memory. Adding a new set is a content-only
/// change: drop a manifest + image folder, rebuild. The README §Card sets
/// documents the on-disk schema; the on-wire shape lives in
/// <see cref="CardSetManifestDto"/>.
///
/// Registered as a singleton; the implementation also satisfies
/// <see cref="ICardSetCatalog"/> so application-layer services can
/// validate a user-supplied card-set id without depending on
/// <c>Briscola.Api</c>.
/// </summary>
public sealed partial class CardSetCatalog : ICardSetCatalog
{
    public const string PlaceholderId = "placeholder";

    [LoggerMessage(EventId = 1, Level = LogLevel.Warning,
        Message = "Card-set root not found at {Path}. The frontend will render the bundled fallback only.")]
    private static partial void LogRootMissing(ILogger logger, string path);

    [LoggerMessage(EventId = 2, Level = LogLevel.Warning,
        Message = "Card-set directory {Dir} has no manifest.json — skipping.")]
    private static partial void LogManifestMissing(ILogger logger, string dir);

    [LoggerMessage(EventId = 3, Level = LogLevel.Warning,
        Message = "Card-set manifest {Path} is malformed — skipping.")]
    private static partial void LogManifestMalformed(ILogger logger, string path);

    [LoggerMessage(EventId = 4, Level = LogLevel.Warning,
        Message = "Card-set manifest {Path} failed to parse — skipping.")]
    private static partial void LogManifestParseFailed(ILogger logger, Exception ex, string path);

    private readonly ImmutableArray<CardSetManifestDto> _items;
    private readonly HashSet<string> _idIndex;
    private readonly ImmutableArray<string> _idsView;

    public CardSetCatalog(ImmutableArray<CardSetManifestDto> items)
    {
        _items = items;
        _idIndex = items.Select(static m => m.Id).ToHashSet(StringComparer.Ordinal);
        _idsView = items.Select(static m => m.Id).ToImmutableArray();
    }

    public ImmutableArray<CardSetManifestDto> All => _items;

    string ICardSetCatalog.DefaultSetId => PlaceholderId;

    IReadOnlyList<string> ICardSetCatalog.AllIds => _idsView;

    public bool Contains(string id) => _idIndex.Contains(id);

    /// <summary>
    /// Loads every <c>wwwroot/card-sets/&lt;id&gt;/manifest.json</c>. Malformed
    /// manifests are logged at <c>Warning</c> and skipped. The catalog is
    /// permissive by default — the README mandates a hard-fail when the
    /// <c>placeholder</c> set is missing; that check belongs in Program.cs
    /// once step 9.2 has populated the wwwroot fixture (until then the
    /// catalog returns an empty list and the frontend uses its bundled
    /// fallback).
    /// </summary>
    public static CardSetCatalog LoadFromWebRoot(IWebHostEnvironment env, ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(env);
        string webRoot = env.WebRootPath ?? Path.Combine(env.ContentRootPath, "wwwroot");
        string root = Path.Combine(webRoot, "card-sets");
        if (!Directory.Exists(root))
        {
            if (logger is not null) LogRootMissing(logger, root);
            return new CardSetCatalog(ImmutableArray<CardSetManifestDto>.Empty);
        }

        List<CardSetManifestDto> items = [];
        foreach (string dir in Directory.EnumerateDirectories(root))
        {
            string manifestPath = Path.Combine(dir, "manifest.json");
            if (!File.Exists(manifestPath))
            {
                if (logger is not null) LogManifestMissing(logger, Path.GetFileName(dir));
                continue;
            }

            try
            {
                using FileStream stream = File.OpenRead(manifestPath);
                CardSetManifestDto? parsed = JsonSerializer.Deserialize<CardSetManifestDto>(
                    stream, JsonOptionsCache.Web);
                if (parsed is null || string.IsNullOrWhiteSpace(parsed.Id))
                {
                    if (logger is not null) LogManifestMalformed(logger, manifestPath);
                    continue;
                }

                items.Add(parsed with { Path = $"/card-sets/{Path.GetFileName(dir)}/" });
            }
            catch (JsonException ex)
            {
                if (logger is not null) LogManifestParseFailed(logger, ex, manifestPath);
            }
        }

        return new CardSetCatalog([.. items]);
    }
}
