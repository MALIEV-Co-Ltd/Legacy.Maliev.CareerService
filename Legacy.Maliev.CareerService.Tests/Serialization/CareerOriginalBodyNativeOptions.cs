using Legacy.Maliev.CareerService.Application.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Formatters;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Legacy.Maliev.CareerService.Tests.Serialization;

internal static class CareerOriginalBodyNativeOptions
{
    internal const string SetupName = "Legacy.Maliev.CareerService.Api.Serialization.CareerOriginalBodyMvcOptionsSetup";
    internal const string FormatterName = "Legacy.Maliev.CareerService.Api.Serialization.CareerOriginalBodyInputFormatter";

    internal static bool IsOwned(IConfigureOptions<MvcOptions> value) =>
        value.GetType().Assembly == typeof(Program).Assembly && value.GetType().FullName == SetupName;

    internal static MvcOptions Create(IServiceProvider services)
    {
        var setups = services.GetServices<IConfigureOptions<MvcOptions>>().ToArray();
        Assert.Single(setups, IsOwned);
        var retained = setups.Where(setup => !IsOwned(setup)).ToArray();
        Assert.Equal(setups.Length - 1, retained.Length);
        var result = new OptionsFactory<MvcOptions>(retained,
            services.GetServices<IPostConfigureOptions<MvcOptions>>(),
            services.GetServices<IValidateOptions<MvcOptions>>()).Create(Options.DefaultName);
        Assert.DoesNotContain(result.InputFormatters, formatter => formatter.GetType().FullName == FormatterName);
        Assert.Single(result.InputFormatters, formatter => formatter is SystemTextJsonInputFormatter);
        return result;
    }

    internal static void RemoveOwned(IServiceCollection services)
    {
        var before = services.ToArray();
        var owned = before.Where(descriptor => descriptor.ServiceType == typeof(IConfigureOptions<MvcOptions>)
            && descriptor.ImplementationType is { } type && type.Assembly == typeof(Program).Assembly
            && type.FullName == SetupName).ToArray();
        Assert.Single(owned);
        Assert.True(services.Remove(owned[0]));
        var retained = before.Where(descriptor => !ReferenceEquals(descriptor, owned[0])).ToArray();
        Assert.Equal(retained.Length, services.Count);
        for (var index = 0; index < retained.Length; index++) Assert.Same(retained[index], services[index]);
    }

    internal static Task<CareerBodyRead> ReadAsync(IServiceProvider services, MvcOptions options, bool level,
        byte[] bytes, string contentType = "application/json", string prefix = "", int maxErrors = 200)
    {
        var type = level ? typeof(UpsertJobLevelRequest) : typeof(UpsertJobOfferRequest);
        // The exact two types both have a JSON reader in every source/native/candidate case.
        var formatter = options.InputFormatters.First(formatter => formatter is not null
            && (formatter.GetType().FullName == FormatterName || formatter is SystemTextJsonInputFormatter));
        return CareerOriginalInputOracle.ReadBytesAsync(formatter, type,
            services.GetRequiredService<IModelMetadataProvider>(), bytes, contentType, prefix, maxErrors);
    }
}
