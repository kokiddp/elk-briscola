using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Briscola.Infrastructure.Codecs;

/// <summary>
/// <see cref="System.Text.Json"/> doesn't ship a converter for
/// <see cref="ImmutableArray{T}"/> because its default value is a sentinel
/// "uninitialized" struct. This converter reads/writes it as a plain JSON
/// array, materializing through a list builder.
/// </summary>
internal sealed class ImmutableArrayJsonConverter<T> : JsonConverter<ImmutableArray<T>>
{
    public override ImmutableArray<T> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            return ImmutableArray<T>.Empty;
        }

        if (reader.TokenType != JsonTokenType.StartArray)
        {
            throw new JsonException($"Expected StartArray for ImmutableArray<{typeof(T).Name}>, got {reader.TokenType}.");
        }

        var builder = ImmutableArray.CreateBuilder<T>();
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndArray)
            {
                return builder.ToImmutable();
            }
            var value = JsonSerializer.Deserialize<T>(ref reader, options)!;
            builder.Add(value);
        }
        throw new JsonException("Unexpected end of JSON while reading ImmutableArray.");
    }

    public override void Write(Utf8JsonWriter writer, ImmutableArray<T> value, JsonSerializerOptions options)
    {
        writer.WriteStartArray();
        foreach (var item in value)
        {
            JsonSerializer.Serialize(writer, item, options);
        }
        writer.WriteEndArray();
    }
}

/// <summary>Factory so System.Text.Json can mint a converter per element type.</summary>
internal sealed class ImmutableArrayJsonConverterFactory : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert)
        => typeToConvert.IsGenericType
           && typeToConvert.GetGenericTypeDefinition() == typeof(ImmutableArray<>);

    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        var elementType = typeToConvert.GetGenericArguments()[0];
        var converterType = typeof(ImmutableArrayJsonConverter<>).MakeGenericType(elementType);
        return (JsonConverter)Activator.CreateInstance(converterType)!;
    }
}
