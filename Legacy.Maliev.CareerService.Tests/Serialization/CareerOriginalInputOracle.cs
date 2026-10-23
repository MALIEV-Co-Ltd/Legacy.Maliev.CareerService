using System.Buffers;
using System.Text;
using Legacy.Maliev.CareerService.Application.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Formatters;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.NewtonsoftJson;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.ObjectPool;
using Newtonsoft.Json;
using Stj = System.Text.Json.JsonSerializer;

namespace Legacy.Maliev.CareerService.Tests.Serialization;

// The three official implementation files are byte-identical at aspnetcore
// v8.0.3, v8.0.28 and v10.0.12. This executes the real stock formatter with
// independently defined original-shaped models and original MVC settings.
internal static class CareerOriginalInputOracle
{
    internal static MvcNewtonsoftJsonOptions OriginalOptions()
    {
        var options = new MvcNewtonsoftJsonOptions();
        options.SerializerSettings.NullValueHandling = NullValueHandling.Ignore;
        options.SerializerSettings.ReferenceLoopHandling = ReferenceLoopHandling.Ignore;
        return options;
    }

    internal static NewtonsoftJsonInputFormatter Formatter()
    {
        var options = OriginalOptions();
        return new NewtonsoftJsonInputFormatter(NullLogger<NewtonsoftJsonInputFormatter>.Instance,
            options.SerializerSettings, ArrayPool<char>.Shared, new DefaultObjectPoolProvider(), new MvcOptions(), options);
    }

    internal static Task<CareerBodyRead> ReadAsync(bool level, string json, string contentType = "application/json", string prefix = "") =>
        ReadBytesAsync(Formatter(), level ? typeof(OriginalLevelBody) : typeof(OriginalOfferBody),
            new EmptyModelMetadataProvider(), Encoding.UTF8.GetBytes(json), contentType, prefix);

    internal static async Task<CareerBodyRead> ReadBytesAsync(IInputFormatter formatter, Type modelType,
        IModelMetadataProvider metadataProvider, byte[] bytes, string contentType, string prefix = "", int maxErrors = 200)
    {
        var http = new DefaultHttpContext();
        using var input = new MemoryStream(bytes, writable: false);
        http.Request.Body = input;
        http.Request.ContentType = contentType;
        http.Request.ContentLength = bytes.LongLength;
        var state = new ModelStateDictionary { MaxAllowedErrors = maxErrors };
        var context = new InputFormatterContext(http, prefix, state, metadataProvider.GetMetadataForType(modelType),
            static (stream, encoding) => new HttpRequestStreamReader(stream, encoding), treatEmptyInputAsDefaultValue: false);
        try
        {
            if (!formatter.CanRead(context)) return Result("unsupported", null, state);
            var result = await formatter.ReadAsync(context);
            return Result(result.HasError ? "failure" : result.IsModelSet ? "success" : "no-value", result.Model, state);
        }
        catch (Exception error)
        {
            // An exact throw type is part of the observation, never treated as a
            // passing malformed-input result by itself. Each caller pins Kind.
            return Result("throw:" + error.GetType().FullName, null, state);
        }
    }

    private static CareerBodyRead Result(string kind, object? model, ModelStateDictionary state) => new(
        kind, model, state.Keys.Order(StringComparer.Ordinal).ToArray(), state.ErrorCount,
        state.Values.SelectMany(value => value.Errors).Select(error => error.Exception?.GetType().FullName ?? "").ToArray(),
        state.Values.SelectMany(value => value.Errors).Select(error => error.ErrorMessage.Length == 0
            ? error.Exception?.Message ?? "" : error.ErrorMessage).ToArray());

    internal static string Projection(object? model) => model switch
    {
        null => "null",
        OriginalLevelBody level => Stj.Serialize(new object?[] { level.Name, level.Description }),
        UpsertJobLevelRequest level => Stj.Serialize(new object?[] { level.Name, level.Description }),
        OriginalOfferBody offer => Stj.Serialize(new object?[]
        {
            offer.LevelId, offer.Title, offer.Introduction, offer.Description,
            offer.Prerequisites, offer.WhatWeOffer, offer.Location, offer.IsFilled
        }),
        UpsertJobOfferRequest offer => Stj.Serialize(new object?[]
        {
            offer.LevelId, offer.Title, offer.Introduction, offer.Description,
            offer.Prerequisites, offer.WhatWeOffer, offer.Location, offer.IsFilled
        }),
        _ => throw new InvalidOperationException("Unexpected fixture model type.")
    };

    internal static string NestedUnknown(int overallDepth) =>
        "{\"Name\":\"N\",\"Description\":\"D\",\"extra\":" +
        new string('[', overallDepth - 1) + "0" + new string(']', overallDepth - 1) + "}";
}

internal sealed record CareerBodyRead(string Kind, object? Model, string[] Keys, int ErrorCount,
    string[] ExceptionTypes, string[] Messages);

#nullable disable
// Mirrors the committed scaffolded source model's oblivious reference metadata.
// No EF types, validation attributes, source credentials or database writes.
internal sealed class OriginalOfferBody
{
    public int Id { get; set; }
    public int LevelId { get; set; }
    public string Title { get; set; }
    public string Introduction { get; set; }
    public string Description { get; set; }
    public string Prerequisites { get; set; }
    public string WhatWeOffer { get; set; }
    public string Location { get; set; }
    public bool? IsFilled { get; set; }
    public DateTime? CreatedDate { get; set; }
    public DateTime? ModifiedDate { get; set; }
    public OriginalLevelBody Level { get; set; }
}

internal sealed class OriginalLevelBody
{
    public OriginalLevelBody() => Offers = new HashSet<OriginalOfferBody>();
    public int Id { get; set; }
    public string Name { get; set; }
    public string Description { get; set; }
    public DateTime? CreatedDate { get; set; }
    public DateTime? ModifiedDate { get; set; }
    public ICollection<OriginalOfferBody> Offers { get; set; }
}
#nullable restore
