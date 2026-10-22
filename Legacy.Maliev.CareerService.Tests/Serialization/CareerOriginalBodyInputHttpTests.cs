using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Legacy.Maliev.CareerService.Domain;
using Legacy.Maliev.CareerService.Tests.Controllers;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Legacy.Maliev.CareerService.Tests.Serialization;

/// <summary>Checks original JSON grammar and graph validation through the complete HTTP pipeline.</summary>
/// <param name="fixture">The shared Career route fixture providing the application factory, request clients, and test database access.</param>
[Collection("Career shared level")]
public sealed class CareerOriginalBodyInputHttpTests(CareerRouteFixture fixture) : IClassFixture<CareerRouteFixture>
{
    private const int ForgedId = 2147483000;
    private const string Marker = "private-input-marker";
    private const string Title = "ช่าง Fixture 🔧";

    /// <summary>Provides accepted grammar variants and ignored-graph bodies.</summary>
    /// <returns>Theory rows combining offer or level writes, create or update mode, and each accepted original JSON grammar or UTF-16 variant.</returns>
    public static IEnumerable<object[]> AcceptedBodies()
    {
        foreach (var kind in new[] { "offer", "level" })
        {
            foreach (var update in new[] { false, true })
            {
                foreach (var grammar in new[] { "single-quotes", "unquoted-names", "comments", "trailing-comma", "second-object", "trailing-garbage", "utf16" })
                {
                    yield return [kind, update, grammar];
                }
            }
        }
    }

    /// <summary>Checks original grammar, write whitelists, and public readback.</summary>
    /// <param name="kind">The resource to write: offer at /Jobs or level at /jobs/levels.</param>
    /// <param name="update">Whether to PUT the seeded record rather than POST a new record.</param>
    /// <param name="grammar">The accepted body variant: single-quotes, unquoted-names, comments, trailing-comma, second-object, trailing-garbage, or utf16.</param>
    /// <returns>A task that completes when write status, persisted whitelist values, ignored graph data, timestamps, and public readback assertions finish.</returns>
    [Theory]
    [MemberData(nameof(AcceptedBodies))]
    public async Task OriginalGrammarAndIgnoredGraph_PreserveWhitelistAndPublicReads(string kind, bool update, string grammar)
    {
        var (levelId, selectedId, siblingId) = await Seed();
        var selectedBefore = await Offer(selectedId);
        var siblingBefore = await Offer(siblingId);
        var levelsBefore = await Levels();
        var offersBefore = await Offers();
        await using var beforeContext = fixture.Context();
        var originalOffer = await beforeContext.Offers.AsNoTracking().SingleAsync(row => row.Id == selectedId);
        var originalLevel = await beforeContext.Levels.AsNoTracking().SingleAsync();
        using var reader = fixture.Client();
        using var primedDetail = await reader.GetAsync($"/Jobs/{selectedId}");
        using var primedList = await reader.GetAsync("/Jobs?index=1&size=10");
        Assert.Equal(HttpStatusCode.OK, primedDetail.StatusCode);
        Assert.Equal(HttpStatusCode.OK, primedList.StatusCode);
        Assert.Equal("false", await reader.GetStringAsync("/Jobs/job-opening-status"));
        var json = Payload(kind, levelId);
        var body = grammar switch
        {
            "single-quotes" => json.Replace('"', '\''),
            "unquoted-names" => System.Text.RegularExpressions.Regex.Replace(json, "\"([A-Za-z][A-Za-z0-9]*)\":", "$1:"),
            "comments" => "/* source body */" + json.Insert(1, "/* source member */"),
            "trailing-comma" => json.Insert(json.Length - 1, ","),
            "second-object" => json + "{\"Id\":\"private-input-marker\"}",
            "trailing-garbage" => json + " private-input-marker",
            "utf16" => json,
            _ => throw new ArgumentOutOfRangeException(nameof(grammar)),
        };
        var route = Route(kind);
        var selected = kind == "offer" ? selectedId : levelId;
        var started = DateTime.UtcNow.AddSeconds(-5);
        using var writer = fixture.Client(Permission(kind, update ? "update" : "create"));
        using var content = new StringContent(body, grammar == "utf16" ? Encoding.Unicode : Encoding.UTF8, "application/json");
        using var response = update ? await writer.PutAsync(route + "/" + selected, content) : await writer.PostAsync(route, content);
        Assert.Equal(update ? HttpStatusCode.NoContent : HttpStatusCode.Created, response.StatusCode);
        var id = selected;
        if (update)
        {
            Assert.Equal(string.Empty, await response.Content.ReadAsStringAsync());
        }
        else
        {
            using var created = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            id = created.RootElement.GetProperty("id").GetInt32();
            Assert.True(id > 0);
            Assert.NotEqual(selected, id);
            Assert.NotEqual(ForgedId, id);
            Assert.EndsWith("/" + id, response.Headers.Location!.ToString(), StringComparison.Ordinal);
        }
        await using var db = fixture.Context();
        if (kind == "offer")
        {
            var row = await db.Offers.AsNoTracking().SingleAsync(item => item.Id == id);
            Assert.Equal(levelId, row.LevelId);
            Assert.Equal(Title, row.Title);
            Assert.Equal("D", row.Description);
            Assert.Equal("P", row.Prerequisites);
            Assert.False(row.IsFilled);
            Assert.Equal(update ? originalOffer.Introduction : null, row.Introduction);
            Assert.Equal(update ? originalOffer.WhatWeOffer : null, row.WhatWeOffer);
            Assert.Equal(update ? originalOffer.Location : null, row.Location);
            Assert.NotNull(row.ModifiedDate);
            Assert.InRange(row.ModifiedDate.Value, started, DateTime.UtcNow.AddMinutes(1));
            if (update)
            {
                Assert.Equal(originalOffer.CreatedDate, row.CreatedDate);
            }
            else
            {
                Assert.NotNull(row.CreatedDate);
                Assert.InRange(row.CreatedDate.Value, started, DateTime.UtcNow.AddMinutes(1));
                Assert.Equal(selectedBefore, await Offer(selectedId));
            }
            Assert.Equal(siblingBefore, await Offer(siblingId));
            Assert.Equal(levelsBefore, await Levels());
            Assert.Equal(update ? 2 : 3, await db.Offers.CountAsync());
            Assert.Equal(1, await db.Levels.CountAsync());
        }
        else
        {
            var row = await db.Levels.AsNoTracking().SingleAsync(item => item.Id == id);
            Assert.Equal("ระดับใหม่", row.Name);
            Assert.Equal("Level description", row.Description);
            Assert.NotNull(row.ModifiedDate);
            Assert.InRange(row.ModifiedDate.Value, started, DateTime.UtcNow.AddMinutes(1));
            if (update)
            {
                Assert.Equal(originalLevel.CreatedDate, row.CreatedDate);
            }
            else
            {
                Assert.NotNull(row.CreatedDate);
                Assert.InRange(row.CreatedDate.Value, started, DateTime.UtcNow.AddMinutes(1));
                Assert.Equal(JsonSerializer.Serialize(originalLevel), JsonSerializer.Serialize(await db.Levels.AsNoTracking().SingleAsync(item => item.Id == levelId)));
            }
            Assert.Equal(offersBefore, await Offers());
            Assert.Equal(update ? 1 : 2, await db.Levels.CountAsync());
            Assert.Equal(2, await db.Offers.CountAsync());
        }
        Assert.False(await db.Offers.AnyAsync(row => row.Id == ForgedId));
        Assert.False(await db.Levels.AnyAsync(row => row.Id == ForgedId));
        using var detail = await reader.GetAsync(route + "/" + id);
        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
        using var detailJson = JsonDocument.Parse(await detail.Content.ReadAsStringAsync());
        if (kind == "offer")
        {
            Assert.Equal(Title, detailJson.RootElement.GetProperty("title").GetString());
            Assert.False(detailJson.RootElement.GetProperty("isFilled").GetBoolean());
        }
        else
        {
            Assert.Equal("ระดับใหม่", detailJson.RootElement.GetProperty("name").GetString());
        }
        using var list = JsonDocument.Parse(await reader.GetStringAsync("/Jobs?index=1&size=10"));
        var items = list.RootElement.GetProperty("items").EnumerateArray().ToArray();
        Assert.Equal(kind == "offer" && !update ? 3 : 2, items.Length);
        if (kind == "offer")
        {
            Assert.Single(items, item => item.GetProperty("id").GetInt32() == id);
        }
        foreach (var item in items)
        {
            if (kind == "offer" && item.GetProperty("id").GetInt32() == id)
            {
                Assert.Equal(Title, item.GetProperty("title").GetString());
                Assert.False(item.GetProperty("isFilled").GetBoolean());
            }
            if (kind == "level")
            {
                Assert.Equal(update ? "ระดับใหม่" : "Original level", item.GetProperty("level").GetProperty("name").GetString());
            }
        }
        Assert.Equal(kind == "offer" ? "true" : "false", await reader.GetStringAsync("/Jobs/job-opening-status"));
    }

    /// <summary>Provides malformed recognized-graph cases.</summary>
    /// <returns>Theory rows containing the resource, create/update/missing mode, malformed recognized graph member, and expected validation-error path.</returns>
    public static IEnumerable<object[]> InvalidGraphs()
    {
        foreach (var mode in new[] { "create", "update", "missing" })
        {
            yield return ["offer", mode, "\"Level\":{\"Id\":\"private-input-marker\"}", "Level.Id"];
            yield return ["offer", mode, "\"Level\":{\"Name\":{\"private-input-marker\":1}}", "Level.Name"];
            yield return ["offer", mode, "\"Level\":{\"Offers\":[{\"LevelId\":\"private-input-marker\"}]}", "Level.Offers[0].LevelId"];
            yield return ["level", mode, "\"Offers\":[{\"Id\":\"private-input-marker\"}]", "Offers[0].Id"];
            yield return ["level", mode, "\"Offers\":[{\"Title\":{\"private-input-marker\":1}}]", "Offers[0].Title"];
            yield return ["level", mode, "\"Offers\":[{\"Level\":{\"CreatedDate\":\"private-input-marker\"}}]", "Offers[0].Level.CreatedDate"];
        }
    }

    /// <summary>Checks that later values or missing records cannot hide an earlier invalid graph.</summary>
    /// <param name="kind">The resource receiving the malformed graph: offer at /Jobs or level at /jobs/levels.</param>
    /// <param name="mode">The route scenario: create with POST, update the seeded record with PUT, or PUT a missing identifier.</param>
    /// <param name="invalidMember">The raw JSON member fragment containing an invalid nested Level or Offers value, inserted before the otherwise valid payload.</param>
    /// <param name="path">The recognized validation-error member path, including nested property names and collection indices.</param>
    /// <returns>A task that completes when the 400 response, preserved recognized path, redaction, and unchanged database graph assertions finish.</returns>
    [Theory]
    [MemberData(nameof(InvalidGraphs))]
    public async Task InvalidEarlierGraph_CannotBeHiddenByLaterValidOccurrenceOrMissingLookup(string kind, string mode, string invalidMember, string path)
    {
        var (levelId, selectedId, siblingId) = await Seed();
        var before = await Graph();
        var payload = Payload(kind, levelId);
        var body = "{" + invalidMember + "," + payload[1..];
        using var writer = fixture.Client(Permission(kind, mode == "create" ? "create" : "update"));
        using var content = new StringContent(body, Encoding.UTF8, "application/json");
        var id = mode == "missing" ? int.MaxValue : kind == "offer" ? selectedId : levelId;
        using var response = mode == "create" ? await writer.PostAsync(Route(kind), content) : await writer.PutAsync(Route(kind) + "/" + id, content);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var responseBody = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain(Marker, responseBody, StringComparison.Ordinal);
        using var json = JsonDocument.Parse(responseBody);
        Assert.True(json.RootElement.GetProperty("errors").TryGetProperty(path, out _), "Recognized source member paths and indexes must be preserved.");
        Assert.Equal(before, await Graph());
    }

    /// <summary>Provides routes for input-refusal boundary scenarios.</summary>
    /// <returns>Theory rows pairing offer or level routes with create, update, and missing-record write scenarios.</returns>
    public static IEnumerable<object[]> RefusalRoutes()
    {
        foreach (var kind in new[] { "offer", "level" })
        {
            foreach (var mode in new[] { "create", "update", "missing" })
            {
                yield return [kind, mode];
            }
        }
    }

    /// <summary>Checks unsupported charset rejection without payload or charset disclosure to loggers.</summary>
    /// <param name="kind">The resource route under test: offer at /Jobs or level at /jobs/levels.</param>
    /// <param name="mode">The write scenario: POST create, PUT an existing record, or PUT a missing identifier.</param>
    /// <returns>A task that completes when unsupported charset input returns 415 without response or log disclosure and leaves the database unchanged.</returns>
    [Theory]
    [MemberData(nameof(RefusalRoutes))]
    public async Task UnsupportedCharset_Retains415WithoutEchoingCharsetOrPayloadToAnyLogger(string kind, string mode)
    {
        var (levelId, selectedId, siblingId) = await Seed();
        var before = await Graph();
        using var capture = new CaptureProvider();
        await using var factory = fixture.Factory.WithWebHostBuilder(builder => builder.ConfigureLogging(logging => logging.AddProvider(capture)));
        using var client = factory.CreateClient();
        using var authority = fixture.Client(Permission(kind, mode == "create" ? "create" : "update"));
        client.DefaultRequestHeaders.Authorization = authority.DefaultRequestHeaders.Authorization;
        using var content = new StringContent(Payload(kind, levelId), Encoding.UTF8, "application/json");
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json") { CharSet = "x-private-charset-marker" };
        var id = mode == "missing" ? int.MaxValue : kind == "offer" ? selectedId : levelId;
        using var response = mode == "create" ? await client.PostAsync(Route(kind), content) : await client.PutAsync(Route(kind) + "/" + id, content);
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
        var responseBody = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("x-private-charset-marker", responseBody, StringComparison.Ordinal);
        Assert.DoesNotContain("Forged graph", responseBody, StringComparison.Ordinal);
        Assert.Equal(before, await Graph());
        Assert.All(capture.Messages, message =>
        {
            Assert.DoesNotContain("x-private-charset-marker", message, StringComparison.Ordinal);
            Assert.DoesNotContain("Forged graph", message, StringComparison.Ordinal);
        });
    }

    /// <summary>Checks fixed malformed-input error keys without echoing attacker-controlled member names.</summary>
    /// <param name="kind">The resource route under test: offer at /Jobs or level at /jobs/levels.</param>
    /// <param name="mode">The write scenario: POST create, PUT an existing record, or PUT a missing identifier.</param>
    /// <returns>A task that completes when malformed unknown input returns 400 under the fixed $ error key without leaking the attacker key or changing stored data.</returns>
    [Theory]
    [MemberData(nameof(RefusalRoutes))]
    public async Task MalformedUnknownMember_UsesFixedErrorKeyWithoutEchoingAttackerKeyInBodyOrLogs(string kind, string mode)
    {
        var (levelId, selectedId, siblingId) = await Seed();
        var before = await Graph();
        using var capture = new CaptureProvider();
        await using var factory = fixture.Factory.WithWebHostBuilder(builder => builder.ConfigureLogging(logging => logging.AddProvider(capture)));
        using var client = factory.CreateClient();
        using var authority = fixture.Client(Permission(kind, mode == "create" ? "create" : "update"));
        client.DefaultRequestHeaders.Authorization = authority.DefaultRequestHeaders.Authorization;
        using var content = new StringContent("{\"private-key-marker\":[}", Encoding.UTF8, "application/json");
        var id = mode == "missing" ? int.MaxValue : kind == "offer" ? selectedId : levelId;
        using var response = mode == "create" ? await client.PostAsync(Route(kind), content) : await client.PutAsync(Route(kind) + "/" + id, content);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var responseBody = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("private-key-marker", responseBody, StringComparison.Ordinal);
        using var json = JsonDocument.Parse(responseBody);
        Assert.True(json.RootElement.GetProperty("errors").TryGetProperty("$", out _), "Unknown attacker-selected paths must use the fixed fallback key.");
        Assert.Equal(before, await Graph());
        Assert.All(capture.Messages, message => Assert.DoesNotContain("private-key-marker", message, StringComparison.Ordinal));
    }

    private static string Route(string kind) => kind == "offer" ? "/Jobs" : "/jobs/levels";

    private static string Permission(string kind, string action) =>
        "legacy-career." + (kind == "offer" ? "jobs" : "levels") + "." + action;

    private static string Payload(string kind, int levelId)
    {
        var date = new DateTime(1980, 1, 1);
        var graph = new
        {
            Id = ForgedId,
            Name = "Forged graph",
            Description = "Graph description",
            Offers = new[]
            {
                new
                {
                    Id = ForgedId,
                    LevelId = levelId,
                    Title = "Nested graph",
                    CreatedDate = date
                }
            }
        };
        object payload = kind == "offer"
            ? new
            {
                Id = ForgedId,
                CreatedDate = date,
                ModifiedDate = date,
                LevelId = levelId,
                Title,
                Description = "D",
                Prerequisites = "P",
                IsFilled = false,
                Introduction = "Forged rich introduction",
                WhatWeOffer = "Forged rich benefits",
                Location = "Forged rich location",
                Level = graph
            }
            : (object)new
            {
                Id = ForgedId,
                CreatedDate = date,
                ModifiedDate = date,
                Name = "ระดับใหม่",
                Description = "Level description",
                Offers = new[]
                {
                    new
                    {
                        Id = ForgedId,
                        LevelId = levelId,
                        Title = "Nested offer",
                        Level = graph
                    }
                }
            };
        return JsonSerializer.Serialize(payload, new JsonSerializerOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
    }

    private async Task<(int LevelId, int SelectedId, int SiblingId)> Seed()
    {
        await fixture.ResetAsync();
        await using var db = fixture.Context();
        var level = new JobLevel
        {
            Name = "Original level",
            Description = "Original description",
            CreatedDate = new DateTime(2020, 1, 1),
            ModifiedDate = new DateTime(2020, 1, 2)
        };
        db.Levels.Add(level);
        await db.SaveChangesAsync();
        var selected = new JobOffer
        {
            LevelId = level.Id,
            Title = "Original",
            Description = "Original description",
            Prerequisites = "Original prerequisites",
            Introduction = "Imported introduction",
            WhatWeOffer = "Imported benefits",
            Location = "Imported location",
            IsFilled = true,
            CreatedDate = new DateTime(2020, 1, 1),
            ModifiedDate = new DateTime(2020, 1, 2)
        };
        var sibling = new JobOffer
        {
            LevelId = level.Id,
            Title = "Sibling",
            IsFilled = true
        };
        db.Offers.AddRange(selected, sibling);
        await db.SaveChangesAsync();
        Assert.NotEqual(int.MaxValue, selected.Id);
        Assert.NotEqual(int.MaxValue, sibling.Id);
        Assert.NotEqual(int.MaxValue, level.Id);
        Assert.NotEqual(ForgedId, selected.Id);
        Assert.NotEqual(ForgedId, sibling.Id);
        Assert.NotEqual(ForgedId, level.Id);
        return (level.Id, selected.Id, sibling.Id);
    }

    private async Task<string> Offer(int id)
    {
        await using var db = fixture.Context();
        return JsonSerializer.Serialize(await db.Offers.AsNoTracking().Where(row => row.Id == id).Select(row => new
        {
            row.Id,
            row.LevelId,
            row.Title,
            row.Description,
            row.Prerequisites,
            row.Introduction,
            row.WhatWeOffer,
            row.Location,
            row.IsFilled,
            row.CreatedDate,
            row.ModifiedDate,
            Version = EF.Property<uint>(row, "Version")
        }).SingleAsync());
    }

    private async Task<string> Offers()
    {
        await using var db = fixture.Context();
        var ids = await db.Offers.AsNoTracking().OrderBy(row => row.Id).Select(row => row.Id).ToArrayAsync();
        var rows = new List<string>();
        foreach (var id in ids)
        {
            rows.Add(await Offer(id));
        }
        return JsonSerializer.Serialize(rows);
    }

    private async Task<string> Levels()
    {
        await using var db = fixture.Context();
        return JsonSerializer.Serialize(await db.Levels.AsNoTracking().OrderBy(row => row.Id).Select(row => new
        {
            row.Id,
            row.Name,
            row.Description,
            row.CreatedDate,
            row.ModifiedDate,
            Version = EF.Property<uint>(row, "Version")
        }).ToArrayAsync());
    }

    private async Task<string> Graph() => JsonSerializer.Serialize(new { Offers = await Offers(), Levels = await Levels() });

    private sealed class CaptureProvider : ILoggerProvider
    {
        private readonly ConcurrentQueue<string> messages = new();
        public IEnumerable<string> Messages => messages.ToArray();
        public ILogger CreateLogger(string categoryName) => new CaptureLogger(messages);
        public void Dispose() { }

        private sealed class CaptureLogger(ConcurrentQueue<string> messages) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => true;
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                messages.Enqueue(formatter(state, exception) + (exception is null ? "" : " " + exception));
            }
        }
    }
}
