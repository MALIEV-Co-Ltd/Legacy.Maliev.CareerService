using System.Globalization;
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
public sealed class CareerScalarBodyReaderTests(CareerRouteFixture fixture, ITestOutputHelper output)
    : IClassFixture<CareerRouteFixture>
{
    public static IEnumerable<object[]> OfferBodies()
    {
        yield return ["""{}""", 0, "null", null!, null!, null!, "same"];
        yield return ["""{"LevelId":7}""", 7, "null", null!, null!, null!, "same"];
        yield return ["""{"LevelId":"7"}""", 7, "null", null!, null!, null!, "same"];
        yield return ["""{"LevelId":" +7 "}""", 7, "null", null!, null!, null!, "reject:$.LevelId"];
        yield return ["""{"LevelId":null}""", 0, "null", null!, null!, null!, "reject:$.LevelId"];
        yield return ["""{"LevelId":""}""", 0, "null", null!, null!, null!, "reject:$.LevelId"];
        yield return ["""{"LevelId":7,"LEVELID":null}""", 7, "null", null!, null!, null!, "reject:$.LEVELID"];
        yield return ["""{"LevelId":7,"LevelId":8}""", 8, "null", null!, null!, null!, "same"];
        yield return ["""{"IsFilled":"true"}""", 0, "true", null!, null!, null!, "reject:$.IsFilled"];
        yield return ["""{"IsFilled":" FALSE "}""", 0, "false", null!, null!, null!, "reject:$.IsFilled"];
        yield return ["""{"IsFilled":42}""", 0, "true", null!, null!, null!, "reject:$.IsFilled"];
        yield return ["""{"IsFilled":0}""", 0, "false", null!, null!, null!, "reject:$.IsFilled"];
        yield return ["""{"IsFilled":-0.0}""", 0, "false", null!, null!, null!, "reject:$.IsFilled"];
        yield return ["""{"IsFilled":0.5}""", 0, "true", null!, null!, null!, "reject:$.IsFilled"];
        yield return ["""{"IsFilled":true,"IsFilled":null}""", 0, "true", null!, null!, null!, "filled-null"];
        yield return ["""{"IsFilled":false,"IsFilled":""}""", 0, "false", null!, null!, null!, "reject:$.IsFilled"];
        yield return ["""{"IsFilled":null}""", 0, "null", null!, null!, null!, "same"];
        yield return ["""{"Introduction":42,"WhatWeOffer":true,"Location":-0}""", 0, "null", "42", "true", "-0", "reject:$.Introduction"];
        yield return ["""{"Introduction":"I","Introduction":null,"WhatWeOffer":"W","WhatWeOffer":null,"Location":"L","Location":null}""", 0, "null", "I", "W", "L", "rich-null"];
        yield return ["""{"Introduction":"","WhatWeOffer":"  ไทย  ","Location":"Bangkok"}""", 0, "null", "", "  ไทย  ", "Bangkok", "same"];
    }

    [Theory]
    [MemberData(nameof(OfferBodies))]
    public void ScalarProjection_RecordsPublicSourceNativeAndCandidate(
        string json, int levelId, string filled, string? introduction, string? benefits, string? location, string nativeDisposition)
    {
        var source = SourceOffer(json)!;
        Assert.Equal(levelId, source.LevelId);
        Assert.Equal(filled == "null" ? (bool?)null : bool.Parse(filled), source.IsFilled);
        Assert.Equal(introduction, source.Introduction);
        Assert.Equal(benefits, source.WhatWeOffer);
        Assert.Equal(location, source.Location);
        output.WriteLine("CAREER-SCALAR-SOURCE-PUBLIC " + OfferProjection(source));
        var native = Native();
        if (nativeDisposition.StartsWith("reject:", StringComparison.Ordinal))
        {
            var error = Assert.Throws<System.Text.Json.JsonException>(() => Stj.Deserialize<UpsertJobOfferRequest>(json, native));
            Assert.Equal(nativeDisposition["reject:".Length..], error.Path);
            output.WriteLine("CAREER-SCALAR-NATIVE-MVC " + error.GetType().FullName + " " + error.Path);
        }
        else
        {
            var original = Stj.Deserialize<UpsertJobOfferRequest>(json, native)!;
            if (nativeDisposition == "same") Assert.Equal(OfferProjection(source), OfferProjection(original));
            else if (nativeDisposition == "filled-null") Assert.Null(original.IsFilled);
            else
            {
                Assert.Equal("rich-null", nativeDisposition);
                Assert.Null(original.Introduction);
                Assert.Null(original.WhatWeOffer);
                Assert.Null(original.Location);
            }
            output.WriteLine("CAREER-SCALAR-NATIVE-MVC " + OfferProjection(original));
        }
        var candidate = Stj.Deserialize<UpsertJobOfferRequest>(json, Candidate())!;
        Assert.Equal(OfferProjection(source), OfferProjection(candidate));
        output.WriteLine("CAREER-SCALAR-CANDIDATE " + OfferProjection(candidate));
    }

    [Theory]
    [InlineData(false, """{"Id":" +7 ","CreatedDate":"2020-01-02T03:04:05Z","ModifiedDate":null}""")]
    [InlineData(true, """{"Id":7,"Id":null,"CreatedDate":"","ModifiedDate":"01/02/2020"}""")]
    [InlineData(false, """{"CreatedDate":"/Date(0)/","ModifiedDate":"/Date(0+0700)/"}""")]
    [InlineData(true, """{"CreatedDate":"2020-01-01T24:00:00Z","ModifiedDate":"2020-01-01"}""")]
    [InlineData(false, """{"Id":7,"CreatedDate":"2020-01-01","unknown":{"Id":[]}}""")]
    public void RecognizedIgnoredScalars_ValidateWithoutEnteringRequestOrWriter(bool level, string json)
    {
        var native = Native();
        if (level)
        {
            var source = JsonConvert.DeserializeObject<OriginalLevel>(json, SourceSettings())!;
            var candidate = Stj.Deserialize<UpsertJobLevelRequest>(json, Candidate())!;
            Assert.Equal(source.Name, candidate.Name);
            Assert.Equal(source.Description, candidate.Description);
            Assert.Equal(Stj.Serialize(Stj.Deserialize<UpsertJobLevelRequest>(json, native), native), Stj.Serialize(candidate, Candidate()));
        }
        else
        {
            var source = SourceOffer(json)!;
            var candidate = Stj.Deserialize<UpsertJobOfferRequest>(json, Candidate())!;
            Assert.Equal(OfferProjection(source), OfferProjection(candidate));
            Assert.Equal(Stj.Serialize(Stj.Deserialize<UpsertJobOfferRequest>(json, native), native), Stj.Serialize(candidate, Candidate()));
        }
        output.WriteLine("CAREER-SCALAR-IGNORED-PUBLIC-NATIVE-CANDIDATE " + (level ? "level " : "offer ") + json);
    }

    [Theory]
    [InlineData(false, """{"LevelId":true}""", "$.LevelId", true)]
    [InlineData(false, """{"LevelId":2147483648}""", "$.LevelId", true)]
    [InlineData(false, """{"LevelId":1.0}""", "$.LevelId", true)]
    [InlineData(false, """{"LevelId":{},"LevelId":7}""", "$.LevelId", true)]
    [InlineData(false, """{"IsFilled":"not-bool"}""", "$.IsFilled", true)]
    [InlineData(false, """{"IsFilled":[],"IsFilled":true}""", "$.IsFilled", true)]
    [InlineData(false, """{"Introduction":{},"Introduction":"later"}""", "$.Introduction", true)]
    [InlineData(false, """{"WhatWeOffer":[]}""", "$.WhatWeOffer", true)]
    [InlineData(false, """{"Location":{}}""", "$.Location", true)]
    [InlineData(false, """{"Id":"invalid-int","Id":7}""", "$.Id", false)]
    [InlineData(true, """{"Id":[],"Id":7}""", "$.Id", false)]
    [InlineData(false, """{"CreatedDate":"invalid-date","CreatedDate":"2020-01-01"}""", "$.CreatedDate", false)]
    [InlineData(true, """{"CreatedDate":42}""", "$.CreatedDate", false)]
    [InlineData(false, """{"ModifiedDate":{}}""", "$.ModifiedDate", false)]
    [InlineData(true, """{"ModifiedDate":[]}""", "$.ModifiedDate", false)]
    public void InvalidKnownScalarOccurrence_RemainsAReaderFailureBeforeController(
        bool level, string json, string path, bool nativeRejects)
    {
        Assert.ThrowsAny<Newtonsoft.Json.JsonException>(() =>
        {
            if (level) JsonConvert.DeserializeObject<OriginalLevel>(json, SourceSettings());
            else SourceOffer(json);
        });
        var native = Native();
        if (nativeRejects)
            Assert.Throws<System.Text.Json.JsonException>(() => Stj.Deserialize<UpsertJobOfferRequest>(json, native));
        else if (level) Assert.NotNull(Stj.Deserialize<UpsertJobLevelRequest>(json, native));
        else Assert.NotNull(Stj.Deserialize<UpsertJobOfferRequest>(json, native));
        var error = Assert.Throws<System.Text.Json.JsonException>(() =>
        {
            if (level) Stj.Deserialize<UpsertJobLevelRequest>(json, Candidate());
            else Stj.Deserialize<UpsertJobOfferRequest>(json, Candidate());
        });
        Assert.Equal(path, error.Path);
        Assert.DoesNotContain("invalid-int", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("invalid-date", error.Message, StringComparison.Ordinal);
        output.WriteLine("CAREER-SCALAR-REJECT SOURCE-PUBLIC CANDIDATE " + path);
    }

    [Theory]
    [InlineData("""{"CreatedDate":"2020-01-01T00:00:00+99:99"}""")]
    [InlineData("""{"CreatedDate":"2020-01-01T24:00:00.0000000Z"}""")]
    [InlineData("""{"CreatedDate":"/Date(-1-99)/"}""")]
    public void IgnoredDateLexemes_UseTheKnownPublicReaderAcceptance(string json)
    {
        Assert.NotNull(SourceOffer(json));
        Assert.NotNull(Stj.Deserialize<UpsertJobOfferRequest>(json, Candidate()));
    }

    [Fact]
    public void CompleteNativeFactoryAndCandidateWriter_PreserveBothNamedDtos()
    {
        var native = Native();
        CareerLevelNativeOptions.AssertNative(native);
        CareerOfferNativeOptions.AssertNative(native);
        var offer = new UpsertJobOfferRequest(7, "T", "I", "D", "P", "W", "L", true);
        var level = new UpsertJobLevelRequest("N", "D");
        Assert.Equal(Stj.Serialize(offer, native), Stj.Serialize(offer, Candidate()));
        Assert.Equal(Stj.Serialize(level, native), Stj.Serialize(level, Candidate()));
        Assert.Null(Stj.Deserialize<UpsertJobOfferRequest>("null", Candidate()));
        Assert.Null(Stj.Deserialize<UpsertJobLevelRequest>("null", Candidate()));
    }

    [Theory]
    [InlineData("en-US")]
    [InlineData("th-TH")]
    [InlineData("fr-FR")]
    public void ScalarReaders_UseInvariantSourceCulture(string culture)
    {
        var previous = CultureInfo.CurrentCulture;
        var previousUi = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
            const string json = """{"LevelId":" +7 ","IsFilled":0.5,"CreatedDate":"12/31/2020"}""";
            Assert.Equal(OfferProjection(SourceOffer(json)!), OfferProjection(Stj.Deserialize<UpsertJobOfferRequest>(json, Candidate())!));
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
            CultureInfo.CurrentUICulture = previousUi;
        }
    }

    private JsonSerializerOptions Native()
    {
        var services = fixture.Factory.Services;
        var setups = services.GetServices<IConfigureOptions<MvcJsonOptions>>().ToArray();
        Assert.Single(setups.Where(CareerLevelNativeOptions.IsOwned));
        return CareerOfferNativeOptions.Construct(setups.Where(setup => !CareerLevelNativeOptions.IsOwned(setup)).ToArray(),
            services.GetServices<IPostConfigureOptions<MvcJsonOptions>>().ToArray(),
            services.GetServices<IValidateOptions<MvcJsonOptions>>().ToArray()).JsonSerializerOptions;
    }

    private JsonSerializerOptions Candidate() =>
        new(fixture.Factory.Services.GetRequiredService<IOptions<MvcJsonOptions>>().Value.JsonSerializerOptions);

    private static JsonSerializerSettings SourceSettings() => new()
    {
        NullValueHandling = NullValueHandling.Ignore,
        ReferenceLoopHandling = ReferenceLoopHandling.Ignore
    };

    private static OriginalOffer? SourceOffer(string json) => JsonConvert.DeserializeObject<OriginalOffer>(json, SourceSettings());

    private static string OfferProjection(OriginalOffer value) => Stj.Serialize(new
    {
        value.LevelId,
        value.Title,
        value.Introduction,
        value.Description,
        value.Prerequisites,
        value.WhatWeOffer,
        value.Location,
        value.IsFilled
    });

    private static string OfferProjection(UpsertJobOfferRequest value) => Stj.Serialize(new
    {
        value.LevelId,
        value.Title,
        value.Introduction,
        value.Description,
        value.Prerequisites,
        value.WhatWeOffer,
        value.Location,
        value.IsFilled
    });

    private sealed class OriginalOffer
    {
        public int Id { get; set; }
        public int LevelId { get; set; }
        public string? Title { get; set; }
        public string? Introduction { get; set; }
        public string? Description { get; set; }
        public string? Prerequisites { get; set; }
        public string? WhatWeOffer { get; set; }
        public string? Location { get; set; }
        public bool? IsFilled { get; set; }
        public DateTime? CreatedDate { get; set; }
        public DateTime? ModifiedDate { get; set; }
    }

    private sealed class OriginalLevel
    {
        public int Id { get; set; }
        public string? Name { get; set; }
        public string? Description { get; set; }
        public DateTime? CreatedDate { get; set; }
        public DateTime? ModifiedDate { get; set; }
    }
}
