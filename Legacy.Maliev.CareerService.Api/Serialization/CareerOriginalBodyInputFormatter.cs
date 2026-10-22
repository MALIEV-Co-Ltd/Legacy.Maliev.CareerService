using System.Buffers;
using System.Globalization;
using Legacy.Maliev.CareerService.Application.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.AspNetCore.Mvc.Formatters;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.NewtonsoftJson;
using Microsoft.Extensions.ObjectPool;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;

namespace Legacy.Maliev.CareerService.Api.Serialization;

// Application-owned registration: the native comparator removes this exact setup only.
internal sealed class CareerOriginalBodyMvcOptionsSetup(ILoggerFactory loggerFactory) : IConfigureOptions<MvcOptions>
{
    public void Configure(MvcOptions options) =>
        options.InputFormatters.Insert(0, new CareerOriginalBodyInputFormatter(options,
            loggerFactory.CreateLogger<NewtonsoftJsonInputFormatter>()));
}

// Compose the exact stock formatter rather than subclassing it: subclassing changes
// its exception policy. Output formatters and unrelated request types stay on STJ.
internal sealed class CareerOriginalBodyInputFormatter : IInputFormatter,
    IApiRequestFormatMetadataProvider, IInputFormatterExceptionPolicy
{
    private readonly NewtonsoftJsonInputFormatter inner;
    private readonly EmptyModelMetadataProvider metadata = new();

    internal CareerOriginalBodyInputFormatter(MvcOptions options, ILogger logger)
    {
        var original = new MvcNewtonsoftJsonOptions();
        original.SerializerSettings.NullValueHandling = NullValueHandling.Ignore;
        original.SerializerSettings.ReferenceLoopHandling = ReferenceLoopHandling.Ignore;
        // MvcNewtonsoftJsonOptions owns MaxDepth32, TypeNameHandling.None and
        // CheckAdditionalContent=false. Never register global Newtonsoft options.
        inner = new NewtonsoftJsonInputFormatter(new PrivateFormatterLogger(logger),
            original.SerializerSettings, ArrayPool<char>.Shared,
            new DefaultObjectPoolProvider(), options, original);
    }

    public InputFormatterExceptionPolicy ExceptionPolicy => InputFormatterExceptionPolicy.MalformedInputExceptions;

    public bool CanRead(InputFormatterContext context) => SourceType(context.ModelType) is { } type
        && inner.CanRead(Context(context, type, new ModelStateDictionary()));

    public IReadOnlyList<string>? GetSupportedContentTypes(string? contentType, Type objectType) =>
        SourceType(objectType) is { } type ? inner.GetSupportedContentTypes(contentType, type) : null;

    public async Task<InputFormatterResult> ReadAsync(InputFormatterContext context)
    {
        var type = SourceType(context.ModelType)
            ?? throw new InvalidOperationException("Unsupported Career request type.");
        var state = new ModelStateDictionary { MaxAllowedErrors = context.ModelState.MaxAllowedErrors };
        InputFormatterResult result;
        try
        {
            // Delegate ReadAsync, including charset selection and stock buffering.
            result = await inner.ReadAsync(Context(context, type, state));
        }
        finally
        {
            foreach (var entry in state)
            {
                var key = SafePath(entry.Key, context.ModelName, type == typeof(OriginalLevelInput));
                foreach (var error in entry.Value.Errors)
                {
                    if (error.Exception is UnsupportedContentTypeException)
                    {
                        context.ModelState.TryAddModelError(key,
                            new UnsupportedContentTypeException("Unsupported request content type."), context.Metadata);
                    }
                    else
                    {
                        context.ModelState.TryAddModelError(key, "Invalid request body.");
                    }
                }
            }
        }

        if (result.HasError || !result.IsModelSet) return result;
        object? mapped = result.Model switch
        {
            OriginalLevelInput level => new UpsertJobLevelRequest(level.Name, level.Description),
            OriginalOfferInput offer => new UpsertJobOfferRequest(offer.LevelId, offer.Title,
                offer.Introduction, offer.Description, offer.Prerequisites, offer.WhatWeOffer,
                offer.Location, offer.IsFilled),
            null => null,
            _ => throw new InvalidOperationException("Unexpected Career input model.")
        };
        // Only existing DTO scalar slots cross the boundary. Navigation graphs,
        // Id and audit dates never enter EF tracking or application write policy.
        return await InputFormatterResult.SuccessAsync(mapped);
    }

    private InputFormatterContext Context(InputFormatterContext outer, Type type, ModelStateDictionary state) =>
        new(outer.HttpContext, outer.ModelName, state, metadata.GetMetadataForType(type),
            outer.ReaderFactory, outer.TreatEmptyInputAsDefaultValue);

    private static Type? SourceType(Type type) => type == typeof(UpsertJobLevelRequest)
        ? typeof(OriginalLevelInput) : type == typeof(UpsertJobOfferRequest) ? typeof(OriginalOfferInput) : null;

    private static string SafePath(string key, string prefix, bool level)
    {
        if (key.Length > 512 || prefix is not ("" or "request" or "item")) return "$";
        var path = key;
        if (prefix.Length != 0)
        {
            if (key == prefix) return prefix;
            if (!key.StartsWith(prefix + ".", StringComparison.Ordinal)) return "$";
            path = key[(prefix.Length + 1)..];
        }
        if (path.Length == 0) return key;
        var cursor = 0;
        while (cursor < path.Length)
        {
            var start = cursor;
            while (cursor < path.Length && path[cursor] is not ('.' or '[')) cursor++;
            var name = path[start..cursor];
            var last = cursor == path.Length;
            if (name is "$id" or "$ref" or "$type" or "$values") return last ? key : "$";
            var navigationLevel = !level && name.Equals("Level", StringComparison.OrdinalIgnoreCase);
            var navigationOffers = level && name.Equals("Offers", StringComparison.OrdinalIgnoreCase);
            var scalar = name.Equals("Id", StringComparison.OrdinalIgnoreCase)
                || name.Equals("CreatedDate", StringComparison.OrdinalIgnoreCase)
                || name.Equals("ModifiedDate", StringComparison.OrdinalIgnoreCase)
                || (level ? name.Equals("Name", StringComparison.OrdinalIgnoreCase)
                    || name.Equals("Description", StringComparison.OrdinalIgnoreCase)
                    : OfferScalar(name));
            if (scalar) return last ? key : "$";
            if (!navigationLevel && !navigationOffers) return "$";
            if (last) return key;
            if (navigationOffers)
            {
                if (path[cursor++] != '[') return "$";
                var indexStart = cursor;
                while (cursor < path.Length && char.IsAsciiDigit(path[cursor])) cursor++;
                if (cursor == indexStart || cursor >= path.Length || path[cursor] != ']'
                    || !int.TryParse(path.AsSpan(indexStart, cursor - indexStart), NumberStyles.None,
                        CultureInfo.InvariantCulture, out _)) return "$";
                cursor++;
                level = false;
                if (cursor == path.Length) return key;
            }
            else
            {
                level = true;
            }
            if (path[cursor++] != '.' || cursor == path.Length) return "$";
        }
        return "$";
    }

    private static bool OfferScalar(string name) => name.Equals("LevelId", StringComparison.OrdinalIgnoreCase)
        || name.Equals("Title", StringComparison.OrdinalIgnoreCase)
        || name.Equals("Introduction", StringComparison.OrdinalIgnoreCase)
        || name.Equals("Description", StringComparison.OrdinalIgnoreCase)
        || name.Equals("Prerequisites", StringComparison.OrdinalIgnoreCase)
        || name.Equals("WhatWeOffer", StringComparison.OrdinalIgnoreCase)
        || name.Equals("Location", StringComparison.OrdinalIgnoreCase)
        || name.Equals("IsFilled", StringComparison.OrdinalIgnoreCase);

    private sealed class PrivateFormatterLogger(ILogger target) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull =>
            target.BeginScope("Career input formatter");
        public bool IsEnabled(LogLevel logLevel) => target.IsEnabled(logLevel);
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter)
        {
            // Do not invoke the stock message delegate, enumerate its state, or
            // forward an exception object to any configured logging provider.
            target.Log(logLevel, eventId, "Career input formatter diagnostic; exception type {ExceptionType}",
                exception?.GetType().FullName ?? "None");
        }
    }
}

#nullable disable
// Original committed scaffold shapes, independently isolated from EF and public DTOs.
// The source reference metadata was oblivious and its collection constructor used HashSet.
internal sealed class OriginalOfferInput
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
    public OriginalLevelInput Level { get; set; }
}

internal sealed class OriginalLevelInput
{
    public OriginalLevelInput() => Offers = new HashSet<OriginalOfferInput>();
    public int Id { get; set; }
    public string Name { get; set; }
    public string Description { get; set; }
    public DateTime? CreatedDate { get; set; }
    public DateTime? ModifiedDate { get; set; }
    public ICollection<OriginalOfferInput> Offers { get; set; }
}
#nullable restore
