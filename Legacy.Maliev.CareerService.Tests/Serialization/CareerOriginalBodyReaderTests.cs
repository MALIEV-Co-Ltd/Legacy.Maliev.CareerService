using System.Text;
using Legacy.Maliev.CareerService.Tests.Controllers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using Xunit.Abstractions;

namespace Legacy.Maliev.CareerService.Tests.Serialization;

[Collection("Career shared level")]
public sealed class CareerOriginalBodyReaderTests(CareerRouteFixture fixture, ITestOutputHelper output)
    : IClassFixture<CareerRouteFixture>
{
    private const string LevelProjection = """["N","D"]""";

    public static IEnumerable<object[]> Cases()
    {
        yield return ["standard", true, """{"Name":"N","Description":"D"}""", "success", "success", LevelProjection];
        yield return ["single quotes", true, "{'Name':'N','Description':'D'}", "success", "failure", LevelProjection];
        yield return ["unquoted names", true, "{Name:'N',Description:'D'}", "success", "failure", LevelProjection];
        yield return ["comments", true, """{/*fixture*/"Name":"N","Description":"D"}""", "success", "failure", LevelProjection];
        yield return ["trailing comma", true, """{"Name":"N","Description":"D",}""", "success", "failure", LevelProjection];
        yield return ["second document", true, """{"Name":"N","Description":"D"}{"Name":"ignored"}""", "success", "failure", LevelProjection];
        yield return ["trailing text", true, """{"Name":"N","Description":"D"}ignored""", "success", "failure", LevelProjection];
        yield return ["unknown skipped", true, """{"Name":"N","Description":"D","extra":{"$type":null}}""", "success", "success", LevelProjection];
        yield return ["whole null", true, "null", "no-value", "no-value", "null"];
        yield return ["whitespace", true, "  ", "no-value", "failure", "null"];
        yield return ["no body", true, "", "no-value", "no-value", "null"];
        yield return ["empty string root", true, "\"\"", "no-value", "failure", "null"];
        yield return ["wrong string root", true, "\"wrong\"", "failure", "failure", "null"];
        yield return ["wrong array root", true, "[]", "failure", "failure", "null"];
        yield return ["unresolved reference", true, """{"$ref":"missing"}""", "no-value", "success", "null"];
        yield return ["invalid reference type", true, """{"$ref":42}""", "failure", "success", "null"];
        yield return ["reference additional fields", true, """{"$ref":"missing","Name":"N"}""", "failure", "success", "null"];
        yield return ["null reference marker", true, """{"$ref":null,"Name":"N","Description":"D"}""", "success", "success", LevelProjection];
        yield return ["type activation disabled", true, """{"$type":"System.IO.FileInfo, System.Private.CoreLib","Name":"N","Description":"D"}""", "success", "success", LevelProjection];
        yield return ["wrong values wrapper", true, """{"$values":[]}""", "failure", "success", "null"];
        yield return ["same document cycle", true, """{"$id":"L","Name":"N","Description":"D","Offers":[{"Level":{"$ref":"L"}}]}""", "success", "success", LevelProjection];
        yield return ["valid ignored level", false, """{"LevelId":7,"Title":"T","IsFilled":false,"Level":{"Name":false,"Description":23}}""", "success", "success", """[7,"T",null,null,null,null,null,false]"""];
        yield return ["invalid level shape", false, """{"LevelId":7,"Level":42}""", "failure", "success", "null"];
        yield return ["earlier invalid level", false, """{"LevelId":7,"Level":{"Id":"bad"},"Level":null}""", "failure", "success", "null"];
        yield return ["recursive offer scalar", false, """{"LevelId":7,"Level":{"Offers":[{"LevelId":true}]}}""", "failure", "success", "null"];
        yield return ["invalid offers shape", true, """{"Name":"N","Description":"D","Offers":42}""", "failure", "success", "null"];
        yield return ["invalid array child", true, """{"Name":"N","Description":"D","Offers":[{"LevelId":true}]}""", "failure", "success", "null"];
        yield return ["invalid recursive date", true, """{"Name":"N","Description":"D","Offers":[{"Level":{"ModifiedDate":{}}}]}""", "failure", "success", "null"];
        yield return ["ignored null collection", true, """{"Name":"N","Description":"D","Offers":null}""", "success", "success", LevelProjection];
        yield return ["later invalid collection", true, """{"Name":"N","Description":"D","Offers":[{}],"Offers":[{"Id":[]}]}""", "failure", "success", "null"];
        yield return ["earlier invalid collection", true, """{"Name":"N","Description":"D","Offers":[{"Id":[]}],"Offers":[]}""", "failure", "success", "null"];
        yield return ["valid reused collection", true, """{"Name":"N","Description":"D","Offers":[{}],"Offers":[{"IsFilled":"true"}]}""", "success", "success", LevelProjection];
        yield return ["scalar behavior under original grammar", false, "{LevelId:' +7 ',Title:42,Introduction:false,Description:'D',Prerequisites:'P',IsFilled:1}", "success", "failure", """[7,"42","false","D","P",null,null,true]"""];
        yield return ["unexpected metadata exception", true, """{"$type":null}""", "throw:System.NullReferenceException", "success", "null"];
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task ExactOfficialSourceNativeAndCandidate_KeepExplicitExpectedOutcomes(
        string label, bool level, string json, string sourceKind, string nativeKind, string expectedProjection)
    {
        var source = await CareerOriginalInputOracle.ReadAsync(level, json);
        Assert.Equal(sourceKind, source.Kind);
        if (source.Kind == "success") Assert.Equal(expectedProjection, CareerOriginalInputOracle.Projection(source.Model));
        var services = fixture.Factory.Services;
        var nativeOptions = CareerOriginalBodyNativeOptions.Create(services);
        var bytes = Encoding.UTF8.GetBytes(json);
        var native = await CareerOriginalBodyNativeOptions.ReadAsync(services, nativeOptions, level, bytes);
        Assert.Equal(nativeKind, native.Kind);
        var candidate = await CareerOriginalBodyNativeOptions.ReadAsync(services,
            services.GetRequiredService<IOptions<MvcOptions>>().Value, level, bytes);
        Assert.Equal(sourceKind, candidate.Kind);
        if (candidate.Kind == "success") Assert.Equal(expectedProjection, CareerOriginalInputOracle.Projection(candidate.Model));
        if (candidate.Kind == "failure")
        {
            Assert.True(candidate.ErrorCount > 0);
            Assert.DoesNotContain(candidate.Messages, message => message.Contains("bad", StringComparison.Ordinal));
        }
        output.WriteLine("CAREER68 " + label + " SOURCE=" + source.Kind + " NATIVE=" + native.Kind + " CANDIDATE=" + candidate.Kind);
    }

    [Theory]
    [InlineData(31, "success", "success")]
    [InlineData(32, "success", "success")]
    [InlineData(33, "failure", "failure")]
    [InlineData(65, "failure", "failure")]
    public async Task OriginalMvcDepthBoundary_AppliesEvenToUnknownSkippedData(int depth, string sourceKind, string nativeKind)
    {
        var json = CareerOriginalInputOracle.NestedUnknown(depth);
        var source = await CareerOriginalInputOracle.ReadAsync(true, json);
        Assert.Equal(sourceKind, source.Kind);
        var services = fixture.Factory.Services;
        var native = await CareerOriginalBodyNativeOptions.ReadAsync(services, CareerOriginalBodyNativeOptions.Create(services), true, Encoding.UTF8.GetBytes(json));
        Assert.Equal(nativeKind, native.Kind);
        var candidate = await CareerOriginalBodyNativeOptions.ReadAsync(services, services.GetRequiredService<IOptions<MvcOptions>>().Value, true, Encoding.UTF8.GetBytes(json));
        Assert.Equal(sourceKind, candidate.Kind);
    }

    [Theory]
    [InlineData("application/json", "success", false)]
    [InlineData("text/json", "success", false)]
    [InlineData("application/vnd.fixture+json", "success", false)]
    [InlineData("application/json; charset=utf-16", "success", true)]
    [InlineData("application/json; charset=private-charset-marker", "failure", false)]
    [InlineData("application/json; charset=utf-16BE", "failure", false)]
    [InlineData("application/xml", "unsupported", false)]
    public async Task StockMediaAndCharsetSelection_IsScopedAndRetainsUnsupportedType(
        string contentType, string kind, bool unicode)
    {
        const string json = """{"Name":"วิศวกร","Description":"ไทย English"}""";
        var bytes = (unicode ? Encoding.Unicode : Encoding.UTF8).GetBytes(json);
        var source = await CareerOriginalInputOracle.ReadBytesAsync(CareerOriginalInputOracle.Formatter(),
            typeof(OriginalLevelBody), new EmptyModelMetadataProvider(), bytes, contentType);
        Assert.Equal(kind, source.Kind);
        var services = fixture.Factory.Services;
        var candidate = await CareerOriginalBodyNativeOptions.ReadAsync(services,
            services.GetRequiredService<IOptions<MvcOptions>>().Value, true, bytes, contentType);
        Assert.Equal(kind, candidate.Kind);
        if (kind == "failure")
        {
            Assert.Contains(typeof(UnsupportedContentTypeException).FullName!, candidate.ExceptionTypes);
            Assert.DoesNotContain(candidate.Messages, message => message.Contains("private-charset-marker", StringComparison.Ordinal));
        }
        if (kind == "success") Assert.Equal(CareerOriginalInputOracle.Projection(source.Model), CareerOriginalInputOracle.Projection(candidate.Model));
    }

    [Fact]
    public void OfficialDefaults_AreMvcDefaultsRatherThanJsonConvertConveniences()
    {
        var options = CareerOriginalInputOracle.OriginalOptions();
        Assert.Equal(32, options.SerializerSettings.MaxDepth);
        Assert.False(options.SerializerSettings.CheckAdditionalContent);
        Assert.Equal(TypeNameHandling.None, options.SerializerSettings.TypeNameHandling);
        Assert.Equal(NullValueHandling.Ignore, options.SerializerSettings.NullValueHandling);
        Assert.Equal(ReferenceLoopHandling.Ignore, options.SerializerSettings.ReferenceLoopHandling);
        Assert.Equal(MissingMemberHandling.Ignore, options.SerializerSettings.MissingMemberHandling);
        Assert.IsType<CamelCaseNamingStrategy>(Assert.IsType<DefaultContractResolver>(options.SerializerSettings.ContractResolver).NamingStrategy);
        Assert.False(options.ReadJsonWithRequestCulture);
        Assert.Equal(30 * 1024, options.InputFormatterMemoryBufferThreshold);
    }

    [Fact]
    public async Task ReferenceMaps_ArePerReadAndRecognizedPathsKeepPrefixAndIndices()
    {
        var services = fixture.Factory.Services;
        var options = services.GetRequiredService<IOptions<MvcOptions>>().Value;
        var first = await CareerOriginalBodyNativeOptions.ReadAsync(services, options, true,
            Encoding.UTF8.GetBytes("""{"$id":"previous","Name":"N","Description":"D"}"""));
        Assert.Equal("success", first.Kind);
        var second = await CareerOriginalBodyNativeOptions.ReadAsync(services, options, true,
            Encoding.UTF8.GetBytes("""{"$ref":"previous"}"""));
        Assert.Equal("no-value", second.Kind);
        const string json = """{"Offers":[{"LevelId":"invalid-value-marker"}]}""";
        var source = await CareerOriginalInputOracle.ReadAsync(true, json, prefix: "request");
        var candidate = await CareerOriginalBodyNativeOptions.ReadAsync(services, options, true,
            Encoding.UTF8.GetBytes(json), prefix: "request");
        Assert.Equal("failure", source.Kind);
        Assert.Equal("failure", candidate.Kind);
        Assert.Equal(source.Keys, candidate.Keys);
        Assert.Contains("request.Offers[0].LevelId", candidate.Keys);
        Assert.DoesNotContain(candidate.Messages, message => message.Contains("invalid-value-marker", StringComparison.Ordinal));
    }

    [Fact]
    public async Task UnknownAttackerPaths_AreFixedAndErrorBudgetRemainsBounded()
    {
        const string json = """{"private-key-marker":{"x":bad}}""";
        var services = fixture.Factory.Services;
        var candidate = await CareerOriginalBodyNativeOptions.ReadAsync(services,
            services.GetRequiredService<IOptions<MvcOptions>>().Value, true, Encoding.UTF8.GetBytes(json));
        Assert.Equal("failure", candidate.Kind);
        Assert.All(candidate.Keys, key => Assert.Equal("$", key));
        Assert.DoesNotContain(candidate.Messages, message => message.Contains("private-key-marker", StringComparison.Ordinal));
        const string many = """{"Id":"bad","CreatedDate":"bad","ModifiedDate":"bad"}""";
        var bounded = await CareerOriginalBodyNativeOptions.ReadAsync(services,
            services.GetRequiredService<IOptions<MvcOptions>>().Value, true, Encoding.UTF8.GetBytes(many), maxErrors: 2);
        Assert.Equal("failure", bounded.Kind);
        Assert.InRange(bounded.ErrorCount, 1, 2);
    }
}
