using System.Text.Json;
using System.Text.Json.Serialization;

namespace Briscola.Api.IntegrationTests.Rest;

/// <summary>
/// Mirrors the API host's JSON config (camelCase + JsonStringEnumConverter)
/// so REST integration tests deserialize responses the same way real
/// clients do.
/// </summary>
internal static class TestJsonOptions
{
    public static readonly JsonSerializerOptions Default = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };
}
