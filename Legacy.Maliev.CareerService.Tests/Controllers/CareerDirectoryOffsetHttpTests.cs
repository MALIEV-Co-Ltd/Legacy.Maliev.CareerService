using System.Net;
using System.Text.Json;
using Legacy.Maliev.CareerService.Domain;
using Microsoft.EntityFrameworkCore;

namespace Legacy.Maliev.CareerService.Tests.Controllers;

[Collection("Career shared level")]
public sealed class CareerDirectoryOffsetHttpTests(CareerLifecycleFixture fixture)
    : IClassFixture<CareerLifecycleFixture>
{
    [Theory]
    [InlineData(1073741825, 4, 0)]
    [InlineData(1073741825, 4, 1)]
    [InlineData(536870913, 8, 0)]
    [InlineData(536870913, 8, 1)]
    [InlineData(int.MaxValue, 250, 0)]
    [InlineData(int.MaxValue, 250, 1)]
    [InlineData(int.MaxValue, 1, 0)]
    [InlineData(int.MaxValue, 1, 1)]
    [InlineData(int.MaxValue, int.MaxValue, 0)]
    [InlineData(int.MaxValue, int.MaxValue, 1)]
    [InlineData(int.MaxValue, 1073741823, 0)]
    [InlineData(int.MaxValue, 1073741823, 1)]
    [InlineData(3, 4, 0)]
    [InlineData(3, 4, 1)]
    public async Task ExhaustedPublicPage_ReturnsNotFoundWithoutWrappingOrMutatingOffersAndLevels(
        int index, int size, int sort)
    {
        using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        var cancellation = lifetime.Token;
        await fixture.ResetAsync();
        int[] allIds;
        int[] matchingIds;
        await using (var db = fixture.Context())
        {
            var level = new JobLevel
            {
                Name = "วิศวกร", Description = "Synthetic shared level",
                CreatedDate = new DateTime(2020, 1, 1), ModifiedDate = new DateTime(2020, 1, 2),
            };
            var matches = Enumerable.Range(0, 5).Select(value => new JobOffer
            {
                Level = level, Title = $"Boundary vacancy {value}", Introduction = "Synthetic introduction",
                Description = "Synthetic description", Prerequisites = "Synthetic prerequisites",
                WhatWeOffer = "Synthetic offer", Location = "ประเทศไทย", IsFilled = false,
                CreatedDate = new DateTime(2020, 2, 1).AddMinutes(value),
                ModifiedDate = new DateTime(2020, 2, 2).AddMinutes(value),
            }).ToArray();
            db.Offers.AddRange(matches);
            db.Offers.Add(new JobOffer
            {
                Level = level, Title = "Other vacancy", Description = "Unrelated synthetic row",
                IsFilled = true, CreatedDate = new DateTime(2020, 3, 1),
            });
            await db.SaveChangesAsync(cancellation);
            matchingIds = matches.Select(row => row.Id).Order().ToArray();
            allIds = await db.Offers.OrderBy(row => row.Id).Select(row => row.Id).ToArrayAsync(cancellation);
            Assert.Equal(5, matchingIds.Length);
            Assert.Equal(6, allIds.Length);
        }
        if (sort == 1)
        {
            Array.Reverse(allIds);
            Array.Reverse(matchingIds);
        }

        var before = await SnapshotAsync(cancellation);
        using var client = fixture.Client();
        Assert.Null(client.DefaultRequestHeaders.Authorization);
        foreach (var (query, ids) in new[]
        {
            ($"sort={sort}&", allIds),
            ($"sort={sort}&search=Boundary&", matchingIds),
        })
        {
            foreach (var route in new[] { "/Jobs", "/jobs/" })
            {
                await AssertOrdinaryPagesAsync(client, route, query, ids, cancellation);
                using var exhausted = await client.GetAsync($"{route}?{query}index={index}&size={size}", cancellation);
                Assert.Equal(HttpStatusCode.NotFound, exhausted.StatusCode);
                await AssertOrdinaryPagesAsync(client, route, query, ids, cancellation);
            }
        }
        using var status = await client.GetAsync("/Jobs/job-opening-status", cancellation);
        Assert.Equal(HttpStatusCode.OK, status.StatusCode);
        using var statusJson = JsonDocument.Parse(await status.Content.ReadAsStringAsync(cancellation));
        Assert.True(statusJson.RootElement.GetBoolean());
        Assert.Equal(before, await SnapshotAsync(cancellation));
    }

    private static async Task AssertOrdinaryPagesAsync(HttpClient client, string route, string query,
        int[] ids, CancellationToken cancellation)
    {
        // The existing omitted-size rule returns this complete filtered cohort.
        await AssertPageAsync(client, route + "?" + query, ids, 1, ids.Length, cancellation);
        for (var page = 1; page <= 2; page++)
            await AssertPageAsync(client, $"{route}?{query}index={page}&size=4", ids, page, 4, cancellation);
    }

    private static async Task AssertPageAsync(HttpClient client, string path, int[] ids,
        int pageIndex, int pageSize, CancellationToken cancellation)
    {
        using var response = await client.GetAsync(path, cancellation);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellation));
        var root = json.RootElement;
        string[] keys = ["items", "pageIndex", "totalPages", "totalItems", "hasPreviousPage", "hasNextPage"];
        Assert.Equal(keys.Order(StringComparer.Ordinal), root.EnumerateObject().Select(row => row.Name).Order(StringComparer.Ordinal));
        var pages = (int)Math.Ceiling(ids.Length / (double)pageSize);
        Assert.Equal(ids.Length, root.GetProperty("totalItems").GetInt32());
        Assert.Equal(pageIndex, root.GetProperty("pageIndex").GetInt32());
        Assert.Equal(pages, root.GetProperty("totalPages").GetInt32());
        Assert.Equal(pageIndex > 1, root.GetProperty("hasPreviousPage").GetBoolean());
        Assert.Equal(pageIndex < pages, root.GetProperty("hasNextPage").GetBoolean());
        Assert.Equal(ids.Skip((pageIndex - 1) * pageSize).Take(pageSize).ToArray(),
            root.GetProperty("items").EnumerateArray().Select(row => row.GetProperty("id").GetInt32()).ToArray());
    }

    private async Task<string> SnapshotAsync(CancellationToken cancellation)
    {
        await using var db = fixture.Context();
        return JsonSerializer.Serialize(new
        {
            Offers = await db.Offers.AsNoTracking().OrderBy(row => row.Id).Select(row => new
            {
                row.Id, row.LevelId, row.Title, row.Introduction, row.Description, row.Prerequisites,
                row.WhatWeOffer, row.Location, row.IsFilled, row.CreatedDate, row.ModifiedDate,
            }).ToArrayAsync(cancellation),
            Levels = await db.Levels.AsNoTracking().OrderBy(row => row.Id).Select(row => new
            {
                row.Id, row.Name, row.Description, row.CreatedDate, row.ModifiedDate,
            }).ToArrayAsync(cancellation),
            OfferRevisions = await db.Database.SqlQueryRaw<string>(
                "SELECT \"ID\"::text || ':' || xmin::text AS \"Value\" FROM \"Offer\" ORDER BY \"ID\"").ToArrayAsync(cancellation),
            LevelRevisions = await db.Database.SqlQueryRaw<string>(
                "SELECT \"ID\"::text || ':' || xmin::text AS \"Value\" FROM \"Level\" ORDER BY \"ID\"").ToArrayAsync(cancellation),
        });
    }
}
