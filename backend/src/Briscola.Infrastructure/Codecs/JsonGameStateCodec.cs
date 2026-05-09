using System.Text.Json;
using System.Text.Json.Serialization;
using Briscola.Application.Ports;
using Briscola.Domain.State;

namespace Briscola.Infrastructure.Codecs;

/// <summary>
/// Round-trips <see cref="GameState"/> through <see cref="System.Text.Json"/>.
/// Custom converters handle <see cref="System.Collections.Immutable.ImmutableArray{T}"/>
/// (no built-in support) and <see cref="GameOutcome"/> (closed discriminated
/// union). Enums travel as strings so a future enum-value addition doesn't
/// silently re-number on disk.
/// </summary>
public sealed class JsonGameStateCodec : IGameStateCodec
{
    public static JsonSerializerOptions DefaultOptions { get; } = BuildOptions();

    private readonly JsonSerializerOptions _options;

    public JsonGameStateCodec()
        : this(DefaultOptions)
    {
    }

    public JsonGameStateCodec(JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
    }

    public string Serialize(GameState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        return JsonSerializer.Serialize(state, _options);
    }

    public GameState Deserialize(string snapshot)
    {
        ArgumentException.ThrowIfNullOrEmpty(snapshot);
        return JsonSerializer.Deserialize<GameState>(snapshot, _options)
            ?? throw new InvalidOperationException("Deserialized GameState was null.");
    }

    private static JsonSerializerOptions BuildOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.Never,
            WriteIndented = false,
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        options.Converters.Add(new ImmutableArrayJsonConverterFactory());
        options.Converters.Add(new GameOutcomeJsonConverter());
        return options;
    }
}
