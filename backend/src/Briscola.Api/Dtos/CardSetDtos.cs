using System.Diagnostics.CodeAnalysis;

namespace Briscola.Api.Dtos;

/// <summary>
/// Wire shape of <c>wwwroot/card-sets/&lt;id&gt;/manifest.json</c> plus
/// the server-derived <see cref="Path"/> prefix that points clients at
/// the static-asset URL space (<c>/card-sets/&lt;id&gt;/</c>). The
/// canonical schema lives in README §Card sets.
/// </summary>
[ExcludeFromCodeCoverage]
public sealed record CardSetManifestDto(
    string Id,
    string Name,
    string License,
    string Preview,
    string FileExtension,
    string FilePattern,
    string Back,
    string Path);
