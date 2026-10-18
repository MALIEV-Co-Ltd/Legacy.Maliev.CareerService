using System.Buffers;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Legacy.Maliev.CareerService.Application.Models;
using Legacy.Maliev.CareerService.Tests.Controllers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Xunit.Abstractions;
using Stj = System.Text.Json.JsonSerializer;
using MvcJsonOptions = Microsoft.AspNetCore.Mvc.JsonOptions;

namespace Legacy.Maliev.CareerService.Tests.Serialization;

[Collection("Career shared level")]
public sealed class CareerOfferRequestReaderTests(CareerRouteFixture fixture, ITestOutputHelper output)
    : IClassFixture<CareerRouteFixture>
{
    public static IEnumerable<object[]> Bodies()
    {
        foreach (var field in new[] { "Title", "Description", "Prerequisites" })
        {
            yield return [field, "null", "", "null", "same"];
            yield return [field, "\"วิศวกร ไทย English\"", "", "วิศวกร ไทย English", "same"];
            yield return [field, "\"\"", "", "", "same"];
            yield return [field, "\"  literal  \"", "", "  literal  ", "same"];
            yield return [field, "\"2026-10-17T01:02:03Z\"", "", "2026-10-17T01:02:03Z", "same"];
            yield return [field, "1.2300e+02", "", "1.2300e+02", "reject"];
            yield return [field, "-0", "", "-0", "reject"];
            yield return [field, "123456789012345678901234567890", "", "123456789012345678901234567890", "reject"];
            yield return [field, "true", "", "true", "reject"];
            yield return [field, "false", "", "false", "reject"];
            yield return [field, "\"first\"", ",\"" + field.ToUpperInvariant() + "\":null", "first", "native-null"];
            yield return [field, "null", ",\"" + field + "\":\"last\"", "last", "same"];
            yield return [field, "\"first\"", ",\"" + field + "\":\"last\"", "last", "same"];
        }
    }

    [Theory]
    [MemberData(nameof(Bodies))]
    public void ConsumedProjection_MatchesPublicSourceReader(
        string field, string token, string suffix, string expected, string disposition)
    {
        var json = "{\"" + field + "\":" + token + suffix + "}";
        var source = Source(json)!;
        var sourceValue = Value(source, field);
        Assert.Equal(expected == "null" && token == "null" && suffix.Length == 0 ? null : expected, sourceValue);
        output.WriteLine("CAREER-OFFER-SOURCE-PUBLIC " + JsonConvert.SerializeObject(new { Json = json, Value = sourceValue }));
        var native = CareerOfferNativeOptions.Create(fixture.Factory.Services);
        CareerOfferNativeOptions.AssertNative(native);
        if (disposition == "reject")
        {
            var error = Assert.Throws<System.Text.Json.JsonException>(() => Stj.Deserialize<UpsertJobOfferRequest>(json, native));
            Assert.Equal("$." + field, error.Path);
            output.WriteLine("CAREER-OFFER-NATIVE-MVC " + error.GetType().FullName + " " + error.Path);
        }
        else
        {
            var original = Stj.Deserialize<UpsertJobOfferRequest>(json, native)!;
            Assert.Equal(disposition == "native-null" ? null : sourceValue, Value(original, field));
            output.WriteLine("CAREER-OFFER-NATIVE-MVC " + JsonConvert.SerializeObject(new { Disposition = disposition, Value = Value(original, field) }));
        }
        var candidate = Stj.Deserialize<UpsertJobOfferRequest>(json, Candidate())!;
        Assert.Equal(source.Title, candidate.Title);
        Assert.Equal(source.Description, candidate.Description);
        Assert.Equal(source.Prerequisites, candidate.Prerequisites);
        output.WriteLine("CAREER-OFFER-CANDIDATE " + JsonConvert.SerializeObject(new { candidate.Title, candidate.Description, candidate.Prerequisites }));
    }

    [Theory]
    [InlineData("Title", "{}")]
    [InlineData("Title", "[]")]
    [InlineData("Description", "{}")]
    [InlineData("Description", "[]")]
    [InlineData("Prerequisites", "{}")]
    [InlineData("Prerequisites", "[]")]
    public void InvalidEarlierOccurrence_CannotBeHiddenByLaterText(string field, string token)
    {
        var json = "{\"" + field + "\":" + token + ",\"" + field + "\":\"valid\"}";
        Assert.ThrowsAny<Newtonsoft.Json.JsonException>(() => Source(json));
        var error = Assert.Throws<System.Text.Json.JsonException>(() => Stj.Deserialize<UpsertJobOfferRequest>(json, Candidate()));
        Assert.Equal("$." + field, error.Path);
        Assert.DoesNotContain("valid", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("123")]
    [InlineData("true")]
    [InlineData("\"text\"")]
    public void NonObjectRoots_RemainRejected(string json)
    {
        Assert.ThrowsAny<Newtonsoft.Json.JsonException>(() => Source(json));
        Assert.Throws<System.Text.Json.JsonException>(() => Stj.Deserialize<UpsertJobOfferRequest>(json, Candidate()));
    }

    [Fact]
    public void WholeNullAndUnrelatedTypedFieldsRetainNativeBehavior()
    {
        Assert.Null(Source("null"));
        Assert.Null(Stj.Deserialize<UpsertJobOfferRequest>("null", Candidate()));
        var native = CareerOfferNativeOptions.Create(fixture.Factory.Services);
        var request = new UpsertJobOfferRequest(42, "T", "I", "D", "P", "W", "L", true);
        Assert.Equal(Stj.Serialize(request, native), Stj.Serialize(request, Candidate()));
        // Preserve all five original native controls. The four scalar cases now
        // inside issue66 compare their approved source projection instead.
        foreach (var json in new[] { """{"LevelId":true}""", """{"IsFilled":42}""", """{"Introduction":42}""", """{"WhatWeOffer":true}""", """{"Location":42}""" })
            Assert.Throws<System.Text.Json.JsonException>(() => Stj.Deserialize<UpsertJobOfferRequest>(json, native));
        Assert.Throws<System.Text.Json.JsonException>(() => Stj.Deserialize<UpsertJobOfferRequest>("""{"LevelId":true}""", Candidate()));
        Assert.True(Stj.Deserialize<UpsertJobOfferRequest>("""{"IsFilled":42}""", Candidate())!.IsFilled);
        Assert.Equal("42", Stj.Deserialize<UpsertJobOfferRequest>("""{"Introduction":42}""", Candidate())!.Introduction);
        Assert.Equal("true", Stj.Deserialize<UpsertJobOfferRequest>("""{"WhatWeOffer":true}""", Candidate())!.WhatWeOffer);
        Assert.Equal("42", Stj.Deserialize<UpsertJobOfferRequest>("""{"Location":42}""", Candidate())!.Location);
        Assert.Throws<System.Text.Json.JsonException>(() => Stj.Deserialize<JobOfferResponse>("""{"Title":42}""", Candidate()));
        // Removing the Offer registration must retain the independent Level registration.
        Assert.Equal("42", Stj.Deserialize<UpsertJobLevelRequest>("""{"Name":42}""", native)!.Name);
    }

    [Theory]
    [InlineData("en-US")]
    [InlineData("th-TH")]
    [InlineData("fr-FR")]
    public void NumericLexemes_AreCultureIndependent(string culture)
    {
        var previous = CultureInfo.CurrentCulture;
        var previousUi = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
            const string json = """{"Title":1.2300e+02,"Description":false,"Prerequisites":-0}""";
            var source = Source(json)!;
            var candidate = Stj.Deserialize<UpsertJobOfferRequest>(json, Candidate())!;
            Assert.Equal("1.2300e+02", source.Title);
            Assert.Equal(source.Title, candidate.Title);
            Assert.Equal(source.Description, candidate.Description);
            Assert.Equal(source.Prerequisites, candidate.Prerequisites);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
            CultureInfo.CurrentUICulture = previousUi;
        }
    }

    [Fact]
    public void NumericLexeme_CrossesInputSegmentsWithoutRounding()
    {
        var first = new InputSegment(Encoding.UTF8.GetBytes("{\"Prerequisites\":1.23"));
        var last = first.Append(Encoding.UTF8.GetBytes("00e+02}"));
        var reader = new Utf8JsonReader(new ReadOnlySequence<byte>(first, 0, last, last.Memory.Length), true, default);
        Assert.Equal("1.2300e+02", Stj.Deserialize<UpsertJobOfferRequest>(ref reader, Candidate())!.Prerequisites);
    }

    private JsonSerializerOptions Candidate() =>
        new(fixture.Factory.Services.GetRequiredService<IOptions<MvcJsonOptions>>().Value.JsonSerializerOptions);

    private static string? Value(SourceOffer value, string field) => field switch
    {
        "Title" => value.Title,
        "Description" => value.Description,
        "Prerequisites" => value.Prerequisites,
        _ => throw new ArgumentOutOfRangeException(nameof(field)),
    };

    private static string? Value(UpsertJobOfferRequest value, string field) => field switch
    {
        "Title" => value.Title,
        "Description" => value.Description,
        "Prerequisites" => value.Prerequisites,
        _ => throw new ArgumentOutOfRangeException(nameof(field)),
    };

    private static SourceOffer? Source(string json) => JsonConvert.DeserializeObject<SourceOffer>(json,
        new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore, ReferenceLoopHandling = ReferenceLoopHandling.Ignore });

    private sealed class SourceOffer
    {
        public string? Title { get; set; }
        public string? Description { get; set; }
        public string? Prerequisites { get; set; }
    }

    private sealed class InputSegment : ReadOnlySequenceSegment<byte>
    {
        public InputSegment(ReadOnlyMemory<byte> memory) => Memory = memory;
        public InputSegment Append(ReadOnlyMemory<byte> memory)
        {
            var next = new InputSegment(memory) { RunningIndex = RunningIndex + Memory.Length };
            Next = next;
            return next;
        }
    }
}
