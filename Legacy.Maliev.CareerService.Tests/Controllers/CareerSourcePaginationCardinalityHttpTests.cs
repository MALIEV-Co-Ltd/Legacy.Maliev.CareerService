using System.Net;
using System.Text.Json;
using Legacy.Maliev.CareerService.Domain;
using Microsoft.EntityFrameworkCore;

namespace Legacy.Maliev.CareerService.Tests.Controllers;

[Collection("Career shared level")]
public sealed class CareerSourcePaginationCardinalityHttpTests(CareerLifecycleFixture fixture)
    : IClassFixture<CareerLifecycleFixture>
{
    [Theory]
    [InlineData("/Jobs")]
    [InlineData("/jobs/")]
    public async Task OriginalThousandRecordPagingContract_DefaultAllAndPageEdgesPreservePhysicalRows(string route)
    {
        await fixture.ResetAsync();
        await using (var db = fixture.Context())
        {
            var level = new JobLevel { Name = "Synthetic pagination level", Description = "Synthetic level description" };
            db.Levels.Add(level);
            await db.SaveChangesAsync();
            db.Offers.AddRange(Enumerable.Range(1, 1000).Select(number => new JobOffer
            {
                LevelId = level.Id,
                Title = $"Synthetic offer {number}",
                Description = "Synthetic pagination fixture",
                CreatedDate = new DateTime(2020, 1, 1).AddMinutes(number)
            }));
            await db.SaveChangesAsync();
        }
        int[] expectedIds;
        string before;
        await using (var db = fixture.Context())
        {
            var rows = await db.Offers.AsNoTracking().OrderBy(row => row.Id).ToArrayAsync();
            Assert.Equal(1000, rows.Length);
            expectedIds = rows.Select(row => row.Id).ToArray();
            Assert.Equal(1000, expectedIds.Distinct().Count());
            before = JsonSerializer.Serialize(rows);
        }
        using var client = fixture.Client();
        await AssertPageAsync(client, route, expectedIds, 1, 1, false, false);
        for (var page = 1; page <= 10; page++)
        {
            await AssertPageAsync(client, $"{route}?index={page}&size=100",
                expectedIds.Skip((page - 1) * 100).Take(100).ToArray(), page, 10, page > 1, page < 10);
        }
        await AssertPageAsync(client, route + "?index=28&size=37", [expectedIds[^1]], 28, 28, true, false);
        foreach (var query in new[] { "?index=11&size=100", "?index=29&size=37" })
        {
            using var response = await client.GetAsync(route + query);
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }
        await using var after = fixture.Context();
        Assert.Equal(before, JsonSerializer.Serialize(await after.Offers.AsNoTracking().OrderBy(row => row.Id).ToArrayAsync()));
    }

    private static async Task AssertPageAsync(HttpClient client, string path, int[] ids,
        int index, int totalPages, bool previous, bool next)
    {
        using var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var page = json.RootElement;
        Assert.Equal(1000, page.GetProperty("totalItems").GetInt32());
        Assert.Equal(index, page.GetProperty("pageIndex").GetInt32());
        Assert.Equal(totalPages, page.GetProperty("totalPages").GetInt32());
        Assert.Equal(previous, page.GetProperty("hasPreviousPage").GetBoolean());
        Assert.Equal(next, page.GetProperty("hasNextPage").GetBoolean());
        Assert.False(page.TryGetProperty("Items", out _));
        Assert.Equal(ids, page.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("id").GetInt32()).ToArray());
    }
}
