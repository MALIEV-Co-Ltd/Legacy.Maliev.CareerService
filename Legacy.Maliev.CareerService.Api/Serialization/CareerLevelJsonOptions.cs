using System.Buffers;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Legacy.Maliev.CareerService.Application.Models;
using Microsoft.AspNetCore.Mvc;

namespace Legacy.Maliev.CareerService.Api.Serialization;

/// <summary>Registers property-local readers for the two consumed level request strings.</summary>
public static class CareerLevelJsonOptions
{
    /// <summary>Preserves native DTO traversal and changes only consumed level input metadata.</summary>
    /// <param name="options">The existing MVC JSON options.</param>
    public static void Configure(JsonOptions options)
    {
        var serializer = options.JsonSerializerOptions;
        if (serializer.TypeInfoResolver is CareerLevelRequestJsonResolver) return;
        serializer.TypeInfoResolver = new CareerLevelRequestJsonResolver(
            serializer.TypeInfoResolver ?? throw new InvalidOperationException("MVC JSON metadata resolver is required."));
    }
}

internal sealed class CareerLevelRequestJsonResolver(IJsonTypeInfoResolver inner) : IJsonTypeInfoResolver
{
    private static readonly string[] Fields =
        [nameof(UpsertJobLevelRequest.Name), nameof(UpsertJobLevelRequest.Description)];
    private static readonly JsonConverter<string> TextReader = new CareerLevelConsumedStringReader();

    public JsonTypeInfo? GetTypeInfo(Type type, JsonSerializerOptions options)
    {
        var metadata = inner.GetTypeInfo(type, options);
        if (type != typeof(UpsertJobLevelRequest)) return metadata;
        if (metadata is null || metadata.Kind != JsonTypeInfoKind.Object)
            throw new InvalidOperationException("Native level request object metadata is required.");

        var names = new HashSet<string>(Fields.Select(name => options.PropertyNamingPolicy?.ConvertName(name) ?? name),
            StringComparer.Ordinal);
        if (names.Count != Fields.Length)
            throw new InvalidOperationException("Distinct consumed level property names are required.");
        metadata.CreateObject = static () => new UpsertJobLevelRequest(null, null);
        var configured = 0;
        foreach (var property in metadata.Properties)
        {
            if (!names.Contains(property.Name)) continue;
            if (property.PropertyType != typeof(string) || property.Set is not { } setter)
                throw new InvalidOperationException("Consumed level string setter is required.");
            property.CustomConverter = TextReader;
            property.Set = (target, value) =>
            {
                if (value is not null) setter(target, value);
            };
            configured++;
        }
        if (configured != Fields.Length)
            throw new InvalidOperationException("Exactly two consumed level properties are required.");
        CareerScalarBodyReaders.ConfigureLevel(metadata, options);
        return metadata;
    }
}

internal sealed class CareerLevelConsumedStringReader : JsonConverter<string>
{
    public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.Null:
                return null;
            case JsonTokenType.String:
                return reader.GetString();
            case JsonTokenType.True:
                return "true";
            case JsonTokenType.False:
                return "false";
            case JsonTokenType.Number:
                var lexeme = reader.HasValueSequence
                    ? Encoding.UTF8.GetString(reader.ValueSequence.ToArray())
                    : Encoding.UTF8.GetString(reader.ValueSpan);
                if (double.TryParse(lexeme, NumberStyles.Float, CultureInfo.InvariantCulture, out _))
                    return lexeme;
                break;
        }
        // Native traversal attaches the property path. Never replay the body or expose input values.
        throw new JsonException("Expected a compatible level string primitive.");
    }

    public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options)
        => writer.WriteStringValue(value);
}
