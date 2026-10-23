using System.Buffers;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Legacy.Maliev.CareerService.Application.Models;
using Microsoft.AspNetCore.Mvc;

namespace Legacy.Maliev.CareerService.Api.Serialization;

/// <summary>Registers property-local readers for the six offer request strings without expanding the write whitelist.</summary>
public static class CareerOfferJsonOptions
{
    /// <summary>Preserves native DTO traversal and changes only consumed offer input metadata.</summary>
    /// <param name="options">The existing MVC JSON options.</param>
    public static void Configure(JsonOptions options)
    {
        var serializer = options.JsonSerializerOptions;
        if (serializer.TypeInfoResolver is CareerOfferRequestJsonResolver) return;
        serializer.TypeInfoResolver = new CareerOfferRequestJsonResolver(
            serializer.TypeInfoResolver ?? throw new InvalidOperationException("MVC JSON metadata resolver is required."));
    }
}

internal sealed class CareerOfferRequestJsonResolver(IJsonTypeInfoResolver inner) : IJsonTypeInfoResolver
{
    private static readonly string[] Fields =
        [nameof(UpsertJobOfferRequest.Title), nameof(UpsertJobOfferRequest.Description), nameof(UpsertJobOfferRequest.Prerequisites), nameof(UpsertJobOfferRequest.Introduction), nameof(UpsertJobOfferRequest.WhatWeOffer), nameof(UpsertJobOfferRequest.Location)];
    private static readonly JsonConverter<string> TextReader = new CareerOfferConsumedStringReader();

    public JsonTypeInfo? GetTypeInfo(Type type, JsonSerializerOptions options)
    {
        var metadata = inner.GetTypeInfo(type, options);
        if (type != typeof(UpsertJobOfferRequest)) return metadata;
        if (metadata is null || metadata.Kind != JsonTypeInfoKind.Object)
            throw new InvalidOperationException("Native offer request object metadata is required.");

        var names = new HashSet<string>(Fields.Select(name => options.PropertyNamingPolicy?.ConvertName(name) ?? name),
            StringComparer.Ordinal);
        if (names.Count != Fields.Length)
            throw new InvalidOperationException("Distinct consumed offer property names are required.");
        metadata.CreateObject = static () => new UpsertJobOfferRequest(0, null, null, null, null, null, null, null);
        var configured = 0;
        foreach (var property in metadata.Properties)
        {
            if (!names.Contains(property.Name)) continue;
            if (property.PropertyType != typeof(string) || property.Set is not { } setter)
                throw new InvalidOperationException("Consumed offer string setter is required.");
            property.CustomConverter = TextReader;
            property.Set = (target, value) =>
            {
                if (value is not null) setter(target, value);
            };
            configured++;
        }
        if (configured != Fields.Length)
            throw new InvalidOperationException("Exactly six offer string properties are required.");
        CareerScalarBodyReaders.ConfigureOffer(metadata, options);
        return metadata;
    }
}

internal sealed class CareerOfferConsumedStringReader : JsonConverter<string>
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
        throw new JsonException("Expected a compatible offer string primitive.");
    }

    public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options)
        => writer.WriteStringValue(value);
}
