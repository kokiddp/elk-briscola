using System.Diagnostics.CodeAnalysis;

namespace Briscola.Api.Dtos;

[ExcludeFromCodeCoverage]
public sealed record CardSetManifestDto(
    string Id,
    string DisplayName,
    string Path);
