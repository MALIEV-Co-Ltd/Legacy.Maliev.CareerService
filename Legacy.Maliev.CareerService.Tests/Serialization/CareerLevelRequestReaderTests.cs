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
public sealed class CareerLevelRequestReaderTests(CareerRouteFixture fixture, ITestOutputHelper output)
    : IClassFixture<CareerRouteFixture>
{
    public static IEnumerable<object[]> Bodies()
    {
        yield return ["""{}""", "same"];
        yield return ["""{"\u004eame":"N","Description":"D","unknown":{"Name":[]}}""", "same"];
        yield return ["""{"Name":"วิศวกร","Description":"ไทย English"}""", "same"];
        yield return ["""{"Name":null,"Description":null}""", "same"];
        yield return ["""{"Name":"","Description":"  Literal level  "}""", "same"];
        yield return ["""{"Name":"N","Name":null,"Description":"D"}""", "name-null"];
        yield return ["""{"Name":"N","Description":"D","Description":null}""", "description-null"];
        yield return ["""{"Name":null,"Name":"N","Description":"D"}""", "same"];
        yield return ["""{"Name":"N","Name":"N2","Description":"D"}""", "same"];
        yield return ["""{"Name":"N","nAmE":null,"DESCRIPTION":"D"}""", "name-null"];
        yield return ["""{"name":"N","NAME":"N2","description":"D"}""", "same"];
        yield return ["""{"Name":123,"Description":true}""", "reject"];
        yield return ["""{"Name":false,"Description":-12.50}""", "reject"];
        yield return ["""{"Name":1e+02,"Description":-0}""", "reject"];
        yield return ["""{"Name":123456789012345678901234567890,"Description":0.0000100}""", "reject"];
        yield return ["""{"Name":123,"Name":null,"Description":false,"Description":null}""", "reject"];
        yield return ["""{"Name":"2026-10-17T01:02:03Z","Description":"\n"}""", "same"];
    }

    [Theory]
    [MemberData(nameof(Bodies))]
    public void ConsumedProjection_MatchesPublicSourceReader(string json, string nativeDisposition)
    {
        var source = Source(json);
        Assert.NotNull(source);
        output.WriteLine("CAREER-LEVEL-SOURCE-PUBLIC " +
            JsonConvert.SerializeObject(new { Json = json, source.Name, source.Description }));

        // This is fresh native MVC configuration inside the candidate host, not a
        // separately executed unmodified-target head or a deployed historical host.
        var native = CareerLevelNativeOptions.Create(fixture.Factory.Services);
        CareerLevelNativeOptions.AssertNative(native);
        if (nativeDisposition == "reject")
        {
            var error = Assert.Throws<System.Text.Json.JsonException>(() =>
                Stj.Deserialize<UpsertJobLevelRequest>(json, native));
            Assert.Equal("$.Name", error.Path);
            output.WriteLine("CAREER-LEVEL-NATIVE-MVC " +
                JsonConvert.SerializeObject(new
                {
                    Disposition = nativeDisposition,
                    ErrorType = error.GetType().FullName,
                    error.Path
                }));
        }
        else
        {
            var nativeValue = Stj.Deserialize<UpsertJobLevelRequest>(json, native);
            Assert.NotNull(nativeValue);
            switch (nativeDisposition)
            {
                case "same":
                    Assert.Equal(source.Name, nativeValue.Name);
                    Assert.Equal(source.Description, nativeValue.Description);
                    break;
                case "name-null":
                    Assert.Equal("N", source.Name);
                    Assert.Equal("D", source.Description);
                    Assert.Null(nativeValue.Name);
                    Assert.Equal("D", nativeValue.Description);
                    break;
                case "description-null":
                    Assert.Equal("N", source.Name);
                    Assert.Equal("D", source.Description);
                    Assert.Equal("N", nativeValue.Name);
                    Assert.Null(nativeValue.Description);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(nativeDisposition),
                        nativeDisposition, "Unknown synthetic fixture disposition.");
            }
            output.WriteLine("CAREER-LEVEL-NATIVE-MVC " +
                JsonConvert.SerializeObject(new
                {
                    Disposition = nativeDisposition,
                    nativeValue.Name,
                    nativeValue.Description
                }));
        }

        var target = Stj.Deserialize<UpsertJobLevelRequest>(json, Candidate());
        Assert.NotNull(target);
        output.WriteLine("CAREER-LEVEL-CANDIDATE " +
            JsonConvert.SerializeObject(new { target.Name, target.Description }));
        Assert.Equal(source.Name, target.Name);
        Assert.Equal(source.Description, target.Description);
    }

    [Fact]
    public void SourceNullIgnore_RetainsPriorNonNullAssignments()
    {
        var source = Source("""{"Name":"N","Name":null,"Description":"D","Description":null}""");
        Assert.NotNull(source);
        Assert.Equal("N", source.Name);
        Assert.Equal("D", source.Description);
    }

    [Theory]
    [InlineData("""{"Name":[]}""")]
    [InlineData("""{"Description":{}}""")]
    [InlineData("""{"Name":"N","Name":[]}""")]
    [InlineData("""{"Description":"D","Description":{}}""")]
    public void ConsumedContainers_RejectWithoutProducingARequest(string json)
    {
        Assert.ThrowsAny<Newtonsoft.Json.JsonException>(() => Source(json));
        Assert.Throws<System.Text.Json.JsonException>(() => Stj.Deserialize<UpsertJobLevelRequest>(json, Candidate()));
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("123")]
    [InlineData("true")]
    [InlineData("\"text\"")]
    public void NonObjectRoots_RemainRejected(string json)
    {
        Assert.ThrowsAny<Newtonsoft.Json.JsonException>(() => Source(json));
        Assert.Throws<System.Text.Json.JsonException>(() => Stj.Deserialize<UpsertJobLevelRequest>(json, Candidate()));
    }

    [Fact]
    public void WholeNull_RemainsNullForTheUnchangedControllerGuard()
    {
        Assert.Null(Source("null"));
        Assert.Null(Stj.Deserialize<UpsertJobLevelRequest>("null", Candidate()));
    }

    [Fact]
    public void ConstructorWritesAndUnrelatedTypesRetainNativeBehavior()
    {
        var request = new UpsertJobLevelRequest("N", "D");
        var (name, description) = request;
        Assert.Equal("N", name);
        Assert.Equal("D", description);
        var native = CareerLevelNativeOptions.Create(fixture.Factory.Services);
        CareerLevelNativeOptions.AssertNative(native);
        Assert.Equal(Stj.Serialize(request, native), Stj.Serialize(request, Candidate()));
        Assert.Throws<System.Text.Json.JsonException>(() =>
            Stj.Deserialize<UnrelatedInput>("""{"Value":42}""", Candidate()));
        Assert.Throws<System.Text.Json.JsonException>(() =>
            Stj.Deserialize<JobLevelResponse>("""{"Name":42}""", Candidate()));
    }

    [Fact]
    public void Registration_IsExactAndNativeOptionsAreFresh()
    {
        var services = fixture.Factory.Services;
        Assert.IsType<OptionsFactory<MvcJsonOptions>>(services.GetRequiredService<IOptionsFactory<MvcJsonOptions>>());
        var expected = typeof(Program).Assembly.GetType(CareerLevelNativeOptions.ConfigurationName) is null ? 0 : 1;
        Assert.Equal(expected, services.GetServices<IConfigureOptions<MvcJsonOptions>>()
            .Count(CareerLevelNativeOptions.IsOwned));
        var native = CareerLevelNativeOptions.Create(services);
        CareerLevelNativeOptions.AssertNative(native);
        Assert.Throws<System.Text.Json.JsonException>(() =>
            Stj.Deserialize<UpsertJobLevelRequest>("""{"Name":42}""", native));
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
            const string json = """{"Name":1.2300e+02,"Description":false}""";
            Assert.Equal("1.2300e+02", Source(json)!.Name);
            Assert.Equal(Source(json)!.Name, Stj.Deserialize<UpsertJobLevelRequest>(json, Candidate())!.Name);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
            CultureInfo.CurrentUICulture = previousUi;
        }
    }

    [Theory]
    [InlineData("Name")]
    [InlineData("Description")]
    public void InvalidEarlierOccurrence_CannotBeHiddenByLaterValidText(string field)
    {
        var json = "{\"" + field + "\":{},\"" + field + "\":\"valid\"}";
        Assert.ThrowsAny<Newtonsoft.Json.JsonException>(() => Source(json));
        var error = Assert.Throws<System.Text.Json.JsonException>(() =>
            Stj.Deserialize<UpsertJobLevelRequest>(json, Candidate()));
        Assert.Equal("$." + field, error.Path);
        Assert.DoesNotContain("valid", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void NumericLexeme_CrossesInputSegmentsWithoutRounding()
    {
        var first = new InputSegment(Encoding.UTF8.GetBytes("{\"Name\":1.23"));
        var last = first.Append(Encoding.UTF8.GetBytes("00e+02}"));
        var sequence = new ReadOnlySequence<byte>(first, 0, last, last.Memory.Length);
        var reader = new Utf8JsonReader(sequence, true, default);
        var result = Stj.Deserialize<UpsertJobLevelRequest>(ref reader, Candidate());
        Assert.Equal("1.2300e+02", result!.Name);
    }

    private JsonSerializerOptions Candidate() =>
        new(fixture.Factory.Services.GetRequiredService<IOptions<MvcJsonOptions>>().Value.JsonSerializerOptions);

    private static SourceLevelText? Source(string json) => JsonConvert.DeserializeObject<SourceLevelText>(json,
        new JsonSerializerSettings
        {
            NullValueHandling = NullValueHandling.Ignore,
            ReferenceLoopHandling = ReferenceLoopHandling.Ignore
        });

    private sealed class SourceLevelText
    {
        public string? Name { get; set; }
        public string? Description { get; set; }
    }

    private sealed record UnrelatedInput(string? Value);

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
