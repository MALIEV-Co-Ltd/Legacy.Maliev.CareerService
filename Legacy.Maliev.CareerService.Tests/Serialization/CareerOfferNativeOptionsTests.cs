using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Legacy.Maliev.CareerService.Application.Models;
using Legacy.Maliev.CareerService.Tests.Controllers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MvcJsonOptions = Microsoft.AspNetCore.Mvc.JsonOptions;

namespace Legacy.Maliev.CareerService.Tests.Serialization;

internal static class CareerOfferNativeOptions
{
    internal const string ConfigurationName = "Legacy.Maliev.CareerService.Api.Serialization.CareerOfferJsonOptions";

    internal static bool IsOwned(IConfigureOptions<MvcJsonOptions> configuration) =>
        configuration is ConfigureNamedOptions<MvcJsonOptions> { Name: "", Action: { } action }
        && action.Target is null
        && action.GetInvocationList().Length == 1
        && action.Method.Name == "Configure"
        && action.Method.DeclaringType?.FullName == ConfigurationName
        && action.Method.DeclaringType?.Assembly == typeof(Program).Assembly;

    private static int ExpectedCount =>
        typeof(Program).Assembly.GetType(ConfigurationName, throwOnError: false) is null ? 0 : 1;

    internal static JsonSerializerOptions Create(IServiceProvider services) =>
        Construct(services.GetServices<IConfigureOptions<MvcJsonOptions>>().ToArray(),
            services.GetServices<IPostConfigureOptions<MvcJsonOptions>>().ToArray(),
            services.GetServices<IValidateOptions<MvcJsonOptions>>().ToArray()).JsonSerializerOptions;

    internal static MvcJsonOptions Construct(IConfigureOptions<MvcJsonOptions>[] setups,
        IPostConfigureOptions<MvcJsonOptions>[] posts, IValidateOptions<MvcJsonOptions>[] validations)
    {
        Assert.Equal(ExpectedCount, setups.Count(IsOwned));
        var retained = setups.Where(setup => !IsOwned(setup)).ToArray();
        Assert.Equal(setups.Length - ExpectedCount, retained.Length);
        var index = 0;
        foreach (var setup in setups)
        {
            if (IsOwned(setup)) continue;
            Assert.Same(setup, retained[index++]);
        }
        Assert.Equal(retained.Length, index);
        return new OptionsFactory<MvcJsonOptions>(retained, posts, validations)
            .Create(Microsoft.Extensions.Options.Options.DefaultName);
    }

    internal static void RemoveOwned(IServiceCollection services)
    {
        var before = services.ToArray();
        var owned = before.Where(descriptor =>
            descriptor.ServiceType == typeof(IConfigureOptions<MvcJsonOptions>)
            && descriptor.ImplementationInstance is IConfigureOptions<MvcJsonOptions> configuration
            && IsOwned(configuration)).ToArray();
        Assert.Equal(ExpectedCount, owned.Length);
        foreach (var descriptor in owned) Assert.True(services.Remove(descriptor));
        var retained = before.Where(descriptor => !owned.Any(item => ReferenceEquals(item, descriptor))).ToArray();
        Assert.Equal(retained.Length, services.Count);
        for (var index = 0; index < retained.Length; index++) Assert.Same(retained[index], services[index]);
    }

    internal static void AssertNative(JsonSerializerOptions options)
    {
        options.MakeReadOnly();
        var metadata = options.GetTypeInfo(typeof(UpsertJobOfferRequest));
        Assert.Equal(JsonTypeInfoKind.Object, metadata.Kind);
        Assert.Null(metadata.CreateObject);
        Assert.All(metadata.Properties, property => Assert.Null(property.CustomConverter));
    }
}

/// <summary>Checks that the offer input adapter preserves unrelated serializer configuration.</summary>
/// <param name="fixture">The shared route fixture supplying the host serializer configuration used to reconstruct native MVC options without the offer adapter.</param>
[Collection("Career shared level")]
public sealed class CareerOfferNativeOptionsTests(CareerRouteFixture fixture) : IClassFixture<CareerRouteFixture>
{
    /// <summary>Checks fresh factories for foreign converter order, names, postconfiguration, and validators.</summary>
    [Fact]
    public void FreshFactory_PreservesForeignOrderNamesPostConfigurationsAndValidators()
    {
        var events = new List<string>();
        var defaults = Microsoft.Extensions.Options.Options.DefaultName;
        var actual = fixture.Factory.Services.GetServices<IConfigureOptions<MvcJsonOptions>>().ToArray();
        IConfigureOptions<MvcJsonOptions>[] setups =
        [
            new ConfigureNamedOptions<MvcJsonOptions>(defaults, _ => events.Add("first")),
            .. actual,
            new ConfigureNamedOptions<MvcJsonOptions>("foreign-name", _ => events.Add("wrong-name")),
            new ConfigureNamedOptions<MvcJsonOptions>(null, _ => events.Add("last")),
        ];
        IPostConfigureOptions<MvcJsonOptions>[] posts =
        [
            .. fixture.Factory.Services.GetServices<IPostConfigureOptions<MvcJsonOptions>>(),
            new PostConfigureOptions<MvcJsonOptions>(defaults, options =>
            {
                events.Add("post0");
                options.AllowInputFormatterExceptionMessages = false;
            }),
            new PostConfigureOptions<MvcJsonOptions>(defaults, options =>
            {
                Assert.False(options.AllowInputFormatterExceptionMessages);
                events.Add("post1");
            }),
        ];
        IValidateOptions<MvcJsonOptions>[] validators =
        [
            .. fixture.Factory.Services.GetServices<IValidateOptions<MvcJsonOptions>>(),
            new ValidateOptions<MvcJsonOptions>(defaults, options =>
            {
                events.Add("validate0");
                return !options.AllowInputFormatterExceptionMessages;
            }, "postconfiguration required"),
            new ValidateOptions<MvcJsonOptions>(defaults, _ => { events.Add("validate1"); return true; }, "second"),
        ];
        var native = CareerOfferNativeOptions.Construct(setups, posts, validators);
        Assert.False(native.AllowInputFormatterExceptionMessages);
        CareerOfferNativeOptions.AssertNative(native.JsonSerializerOptions);
        Assert.Equal(new[] { "first", "last", "post0", "post1", "validate0", "validate1" }, events);
        events.Clear();
        IValidateOptions<MvcJsonOptions>[] failures =
        [
            new ValidateOptions<MvcJsonOptions>(defaults, _ => { events.Add("fail0"); return false; }, "first failure"),
            new ValidateOptions<MvcJsonOptions>(defaults, _ => { events.Add("fail1"); return false; }, "second failure"),
        ];
        var error = Assert.Throws<OptionsValidationException>(() =>
            CareerOfferNativeOptions.Construct(actual, [], failures));
        Assert.Equal(new[] { "first failure", "second failure" }, error.Failures);
        Assert.Equal(new[] { "fail0", "fail1" }, events);
    }
}
