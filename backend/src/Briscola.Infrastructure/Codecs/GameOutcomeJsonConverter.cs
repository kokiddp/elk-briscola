using System.Text.Json;
using System.Text.Json.Serialization;
using Briscola.Domain.State;

namespace Briscola.Infrastructure.Codecs;

/// <summary>
/// Discriminated-union converter for <see cref="GameOutcome"/>. The Domain
/// type intentionally has a private base constructor (closed union) so we
/// can't add <see cref="JsonDerivedTypeAttribute"/>; we route by the "kind"
/// property in the JSON shape:
///   { "kind": "Winner", "seatOrTeam": 0 }
///   { "kind": "Draw" }
/// </summary>
internal sealed class GameOutcomeJsonConverter : JsonConverter<GameOutcome>
{
    public override GameOutcome? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            return null;
        }
        using var doc = JsonDocument.ParseValue(ref reader);
        var root = doc.RootElement;
        var kind = root.GetProperty("kind").GetString()
            ?? throw new JsonException("GameOutcome.kind is required.");

        return kind switch
        {
            "Winner" => new GameOutcome.Winner(root.GetProperty("seatOrTeam").GetInt32()),
            "Draw" => new GameOutcome.Draw(),
            _ => throw new JsonException($"Unknown GameOutcome.kind '{kind}'."),
        };
    }

    public override void Write(Utf8JsonWriter writer, GameOutcome value, JsonSerializerOptions options)
    {
        switch (value)
        {
            case GameOutcome.Winner w:
                writer.WriteStartObject();
                writer.WriteString("kind", "Winner");
                writer.WriteNumber("seatOrTeam", w.SeatOrTeam);
                writer.WriteEndObject();
                break;
            case GameOutcome.Draw:
                writer.WriteStartObject();
                writer.WriteString("kind", "Draw");
                writer.WriteEndObject();
                break;
            default:
                throw new JsonException($"Unknown GameOutcome variant {value.GetType().Name}.");
        }
    }
}
