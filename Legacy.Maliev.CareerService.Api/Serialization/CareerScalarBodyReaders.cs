using System.Buffers;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Legacy.Maliev.CareerService.Application.Models;

namespace Legacy.Maliev.CareerService.Api.Serialization;

// These readers apply only to the existing Offer/Level input metadata. They do not
// register global converters, retain request state, replay bodies or change DTOs.
internal static class CareerScalarBodyReaders
{
    private static readonly JsonConverter<int?> IntegerReader = new ScalarIntegerReader();
    private static readonly JsonConverter<bool?> BooleanReader = new ScalarBooleanReader();
    private static readonly JsonConverter<DateTime?> IgnoredDateReader = new ScalarIgnoredDateReader();

    internal static void ConfigureOffer(JsonTypeInfo metadata, JsonSerializerOptions options)
    {
        var levelName = Name(options, nameof(UpsertJobOfferRequest.LevelId));
        var index = metadata.Properties.Select((property, position) => (property, position))
            .Single(item => item.property.Name == levelName).position;
        var original = metadata.Properties[index];
        if (original.PropertyType != typeof(int) || original.Set is not { } setter || original.Get is not { } getter)
            throw new InvalidOperationException("Native offer LevelId accessors are required.");
        // A nullable input adapter distinguishes null from a real zero without a
        // sentinel or mutable cross-request state. The DTO remains an Int32.
        var input = metadata.CreateJsonPropertyInfo(typeof(int?), original.Name);
        input.CustomConverter = IntegerReader;
        input.Get = getter;
        input.Set = (target, value) =>
        {
            if (value is int number) setter(target, number);
        };
        input.Order = original.Order;
        input.IsRequired = original.IsRequired;
        input.AttributeProvider = original.AttributeProvider;
        input.ShouldSerialize = original.ShouldSerialize;
        metadata.Properties[index] = input;

        var filled = metadata.Properties.Single(property => property.Name == Name(options, nameof(UpsertJobOfferRequest.IsFilled)));
        if (filled.PropertyType != typeof(bool?) || filled.Set is not { } filledSetter)
            throw new InvalidOperationException("Native offer IsFilled setter is required.");
        filled.CustomConverter = BooleanReader;
        filled.Set = (target, value) =>
        {
            if (value is not null) filledSetter(target, value);
        };
        AddIgnoredScalars(metadata, options);
    }

    internal static void ConfigureLevel(JsonTypeInfo metadata, JsonSerializerOptions options) =>
        AddIgnoredScalars(metadata, options);

    private static void AddIgnoredScalars(JsonTypeInfo metadata, JsonSerializerOptions options)
    {
        AddIgnored(metadata, options, "Id", typeof(int?), IntegerReader);
        AddIgnored(metadata, options, "CreatedDate", typeof(DateTime?), IgnoredDateReader);
        AddIgnored(metadata, options, "ModifiedDate", typeof(DateTime?), IgnoredDateReader);
    }

    private static void AddIgnored(JsonTypeInfo metadata, JsonSerializerOptions options, string name, Type type, JsonConverter converter)
    {
        var wireName = Name(options, name);
        if (metadata.Properties.Any(property => property.Name == wireName))
            throw new InvalidOperationException("Ignored source scalar must not replace a DTO member.");
        var property = metadata.CreateJsonPropertyInfo(type, wireName);
        property.CustomConverter = converter;
        property.Set = static (_, _) => { };
        // Write-only metadata validates the recognized source member but never
        // contributes a property to request serialization or a persisted entity.
        property.Get = null;
        property.ShouldSerialize = static (_, _) => false;
        metadata.Properties.Add(property);
    }

    private static string Name(JsonSerializerOptions options, string name) => options.PropertyNamingPolicy?.ConvertName(name) ?? name;

    private static string Number(ref Utf8JsonReader reader) => reader.HasValueSequence
        ? Encoding.UTF8.GetString(reader.ValueSequence.ToArray())
        : Encoding.UTF8.GetString(reader.ValueSpan);

    private static JsonException InvalidScalar() => new("Expected a compatible source scalar.");

    private sealed class ScalarIntegerReader : JsonConverter<int?>
    {
        public override bool HandleNull => true;

        public override int? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Null) return null;
            if (reader.TokenType == JsonTokenType.Number && reader.TryGetInt32(out var number)) return number;
            if (reader.TokenType == JsonTokenType.String)
            {
                var text = reader.GetString();
                if (text!.Length == 0) return null;
                if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out number)) return number;
            }
            throw InvalidScalar();
        }

        public override void Write(Utf8JsonWriter writer, int? value, JsonSerializerOptions options)
        {
            if (value.HasValue) writer.WriteNumberValue(value.Value);
            else writer.WriteNullValue();
        }
    }

    private sealed class ScalarBooleanReader : JsonConverter<bool?>
    {
        public override bool HandleNull => true;

        public override bool? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Null) return null;
            if (reader.TokenType == JsonTokenType.True) return true;
            if (reader.TokenType == JsonTokenType.False) return false;
            if (reader.TokenType == JsonTokenType.String)
            {
                var text = reader.GetString();
                if (text!.Length == 0) return null;
                if (bool.TryParse(text, out var value)) return value;
            }
            if (reader.TokenType == JsonTokenType.Number
                && double.TryParse(Number(ref reader), NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
                return number != 0;
            throw InvalidScalar();
        }

        public override void Write(Utf8JsonWriter writer, bool? value, JsonSerializerOptions options)
        {
            if (value.HasValue) writer.WriteBooleanValue(value.Value);
            else writer.WriteNullValue();
        }
    }

    private sealed class ScalarIgnoredDateReader : JsonConverter<DateTime?>
    {
        public override bool HandleNull => true;

        public override DateTime? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Null) return null;
            if (reader.TokenType == JsonTokenType.String)
            {
                var text = reader.GetString()!;
                if (text.Length == 0) return null;
                // Only validity is consumed. The original controllers discard both
                // supplied audit dates and generate their own server timestamps.
                if (LegacyIsoDate(text) || LegacyMicrosoftDate(text)
                    || DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out _))
                    return DateTime.MinValue;
            }
            throw InvalidScalar();
        }

        public override void Write(Utf8JsonWriter writer, DateTime? value, JsonSerializerOptions options) =>
            throw new InvalidOperationException("Ignored audit input has no writer.");
    }

    private static bool LegacyIsoDate(string text)
    {
        if (text.Length is < 19 or > 40 || text[4] != '-' || text[7] != '-' || text[10] != 'T'
            || text[13] != ':' || text[16] != ':') return false;
        if (!Digits(text, 0, 4, out var year) || year == 0
            || !Digits(text, 5, 2, out var month) || month is < 1 or > 12
            || !Digits(text, 8, 2, out var day) || day < 1 || day > DateTime.DaysInMonth(year, month)
            || !Digits(text, 11, 2, out var hour) || hour > 24
            || !Digits(text, 14, 2, out var minute) || minute > 59
            || !Digits(text, 17, 2, out var second) || second > 59
            || (hour == 24 && (minute != 0 || second != 0))) return false;
        var position = 19;
        var fraction = 0;
        var count = 0;
        if (position < text.Length && text[position] == '.')
        {
            position++;
            while (position < text.Length && count < 7 && text[position] is >= '0' and <= '9')
            {
                fraction = fraction * 10 + text[position++] - '0';
                count++;
            }
            if (count == 0 || (hour == 24 && fraction != 0)) return false;
        }
        if (position < text.Length && text[position] is 'Z' or 'z') position++;
        else
        {
            if (position + 2 < text.Length && text[position] is '+' or '-'
                && Digits(text, position + 1, 2, out _)) position += 3;
            if (position < text.Length && text[position] == ':') position++;
            if (Digits(text, position, 2, out _)) position += 2;
        }
        if (position != text.Length) return false;
        try
        {
            var date = new DateTime(year, month, day, hour == 24 ? 0 : hour, minute, second);
            if (hour == 24) date.AddDays(1);
            return true;
        }
        catch (ArgumentOutOfRangeException) { return false; }
    }

    private static bool LegacyMicrosoftDate(string text)
    {
        if (text.Length < 9 || !text.StartsWith("/Date(", StringComparison.Ordinal) || !text.EndsWith(")/", StringComparison.Ordinal))
            return false;
        var offset = text.IndexOf('+', 7, text.Length - 8);
        if (offset < 0) offset = text.IndexOf('-', 7, text.Length - 8);
        var end = offset < 0 ? text.Length - 2 : offset;
        if (offset >= 0 && (!Digits(text, offset + 1, 2, out _)
            || (text.Length - offset > 5 && !Digits(text, offset + 3, 2, out _)))) return false;
        var ticksText = text.AsSpan(6, end - 6);
        var start = ticksText.Length > 0 && ticksText[0] == '-' ? 1 : 0;
        if (start == ticksText.Length) return false;
        for (var index = start; index < ticksText.Length; index++)
            if (ticksText[index] is < '0' or > '9') return false;
        if (!long.TryParse(ticksText, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var milliseconds)) return false;
        try
        {
            _ = new DateTime(unchecked(milliseconds * TimeSpan.TicksPerMillisecond + DateTime.UnixEpoch.Ticks), DateTimeKind.Utc);
            return true;
        }
        catch (ArgumentOutOfRangeException) { return false; }
    }

    private static bool Digits(string text, int start, int length, out int value)
    {
        value = 0;
        if (start < 0 || start + length > text.Length) return false;
        for (var index = start; index < start + length; index++)
        {
            if (text[index] is < '0' or > '9') return false;
            value = value * 10 + text[index] - '0';
        }
        return true;
    }
}
