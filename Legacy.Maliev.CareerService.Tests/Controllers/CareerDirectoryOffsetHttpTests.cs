using System.Net;
using System.Text.Json;
using Legacy.Maliev.CareerService.Api.Controllers;
using Legacy.Maliev.CareerService.Application.Interfaces;
using Legacy.Maliev.CareerService.Application.Models;
using Legacy.Maliev.CareerService.Domain;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace Legacy.Maliev.CareerService.Tests.Controllers;

/// <summary>Checks directory page boundaries against persisted offers and shared levels.</summary>
/// <param name="fixture">The shared PostgreSQL, Redis, production host, and controlled IAM fixture used for the HTTP assertions.</param>
[Collection("Career shared level")]
public sealed class CareerDirectoryOffsetHttpTests(CareerLifecycleFixture fixture)
    : IClassFixture<CareerLifecycleFixture>
{
    /// <summary>Checks that exhausted pages return 404 without offset wrapping or changing stored rows.</summary>
    /// <param name="index">The one-based page index beyond the available offer pages, including values that can overflow a 32-bit offset.</param>
    /// <param name="size">The requested page size combined with the index to exercise exhausted-page and offset-overflow boundaries.</param>
    /// <param name="sort">The numeric offer identifier sort: 0 for ascending or 1 for descending.</param>
    /// <returns>A task that completes after checking that exhausted pages return 404 without offset wrapping or changing stored rows.</returns>
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
                Name = "วิศวกร",
                Description = "Synthetic shared level",
                CreatedDate = new DateTime(2020, 1, 1),
                ModifiedDate = new DateTime(2020, 1, 2),
            };
            var matches = Enumerable.Range(0, 5).Select(value => new JobOffer
            {
                Level = level,
                Title = $"Boundary vacancy {value}",
                Introduction = "Synthetic introduction",
                Description = "Synthetic description",
                Prerequisites = "Synthetic prerequisites",
                WhatWeOffer = "Synthetic offer",
                Location = "ประเทศไทย",
                IsFilled = false,
                CreatedDate = new DateTime(2020, 2, 1).AddMinutes(value),
                ModifiedDate = new DateTime(2020, 2, 2).AddMinutes(value),
            }).ToArray();
            db.Offers.AddRange(matches);
            db.Offers.Add(new JobOffer
            {
                Level = level,
                Title = "Other vacancy",
                Description = "Unrelated synthetic row",
                IsFilled = true,
                CreatedDate = new DateTime(2020, 3, 1),
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

    /// <summary>Checks the original zero-size 404 result after normal service execution.</summary>
    /// <param name="index">The requested page index used with an explicit page size of zero.</param>
    /// <param name="populated">Whether to seed offers and a shared level before verifying the zero-size response.</param>
    /// <returns>A task that completes after checking the original zero-size 404 result after normal service execution.</returns>
    [Theory]
    [InlineData(0, true)]
    [InlineData(1, true)]
    [InlineData(int.MaxValue, true)]
    [InlineData(0, false)]
    [InlineData(1, false)]
    public async Task ExplicitZeroSize_PreservesSourceNotFoundAfterNormalServiceExecution(int index, bool populated)
    {
        using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        var token = lifetime.Token;
        await SeedZeroSizeAsync(populated, token);
        var before = await SnapshotAsync(token);
        using var client = fixture.Client();
        client.Timeout = TimeSpan.FromSeconds(15);
        Assert.Null(client.DefaultRequestHeaders.Authorization);
        var cache = fixture.Factory.Services.GetRequiredService<IDistributedCache>();
        await PrimeZeroSizeCacheAsync(cache, token);

        // The retained source helper clamps index only and then uses Take(pageSize).
        // This is its zero-cardinality comparator, not a historical SQL Server run.
        const int sourcePageSize = 0;
        var sourceIds = populated ? new[] { 1, 2, 3 } : Array.Empty<int>();
        Assert.Empty(sourceIds.Skip((Math.Max(1, index) - 1) * sourcePageSize).Take(sourcePageSize));

        using var missing = await client.GetAsync("/Jobs/2147483647", token);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        using var expected = JsonDocument.Parse(await missing.Content.ReadAsStringAsync(token));
        foreach (var route in new[] { "/Jobs", "/jobs/" })
            foreach (var query in new[] { "sort=0&", "sort=1&search=Boundary&" })
            {
                using var response = await client.GetAsync($"{route}?{query}index={index}&size=0", token);
                var body = await response.Content.ReadAsStringAsync(token);
                Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
                Assert.Equal(missing.Content.Headers.ContentType?.MediaType, response.Content.Headers.ContentType?.MediaType);
                using var actual = JsonDocument.Parse(body);
                foreach (var key in new[] { "type", "title", "status" })
                    Assert.Equal(expected.RootElement.GetProperty(key).GetRawText(), actual.RootElement.GetProperty(key).GetRawText());
                Assert.False(actual.RootElement.TryGetProperty("items", out _));
                Assert.DoesNotContain("Boundary vacancy", body, StringComparison.Ordinal);
                Assert.Equal(before, await SnapshotAsync(token));
                await AssertZeroSizeCacheAsync(cache, token);
            }
        Assert.Empty(fixture.Authorities);
    }

    /// <summary>Checks that zero-size handling preserves query model-binding errors.</summary>
    /// <param name="query">The query string containing an invalid index, sort, or size value whose binding failure must remain a bad request.</param>
    /// <returns>A task that completes after checking that zero-size handling preserves query model-binding errors.</returns>
    [Theory]
    [InlineData("?size=0&index=not-an-index")]
    [InlineData("?size=0&sort=not-a-sort")]
    [InlineData("?size=not-a-size")]
    public async Task ExplicitZeroSize_DoesNotHideExistingQueryBindingErrors(string query)
    {
        using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        var token = lifetime.Token;
        await SeedZeroSizeAsync(true, token);
        var before = await SnapshotAsync(token);
        using var client = fixture.Client();
        var cache = fixture.Factory.Services.GetRequiredService<IDistributedCache>();
        await PrimeZeroSizeCacheAsync(cache, token);
        using var response = await client.GetAsync("/Jobs" + query, token);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(before, await SnapshotAsync(token));
        await AssertZeroSizeCacheAsync(cache, token);
    }

    /// <summary>Checks that omitted and positive sizes retain anonymous filtered paging contracts.</summary>
    /// <param name="size">The size query value; null omits the parameter to request the default page, while 1 requests a single offer.</param>
    /// <returns>A task that completes after checking that omitted and positive sizes retain anonymous filtered paging contracts.</returns>
    [Theory]
    [InlineData(null)]
    [InlineData("1")]
    public async Task OmittedAndPositiveSizes_RetainAnonymousFilteredPageContracts(string? size)
    {
        using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        var token = lifetime.Token;
        var (allIds, matchingIds) = await SeedZeroSizeAsync(true, token);
        var before = await SnapshotAsync(token);
        using var client = fixture.Client();
        Assert.Null(client.DefaultRequestHeaders.Authorization);
        var cache = fixture.Factory.Services.GetRequiredService<IDistributedCache>();
        await PrimeZeroSizeCacheAsync(cache, token);
        foreach (var route in new[] { "/Jobs", "/jobs/" })
            foreach (var (query, ids) in new[]
            {
                ("sort=0&", allIds),
                ("sort=1&search=Boundary&", matchingIds.Reverse().ToArray()),
            })
            {
                var pageSize = size is null ? ids.Length : 1;
                await AssertPageAsync(client, route + "?" + query + (size is null ? "" : "size=" + size),
                    ids, 1, pageSize, token);
                Assert.Equal(before, await SnapshotAsync(token));
                await AssertZeroSizeCacheAsync(cache, token);
            }
        Assert.Empty(fixture.Authorities);
    }

    /// <summary>Checks exact argument and cancellation forwarding before zero-size requests return 404.</summary>
    /// <returns>A task that completes after checking exact argument and cancellation forwarding before zero-size requests return 404.</returns>
    [Fact]
    public async Task ExplicitZeroSize_ForwardsOriginalArgumentsAndCancellationBeforeReturningNotFound()
    {
        using var cancellation = new CancellationTokenSource();
        var token = cancellation.Token;
        var service = new Mock<ICareerService>(MockBehavior.Strict);
        var page = new PaginatedJobOfferResponse(
            [new JobOfferResponse(901, 1, "Boundary vacancy", null, null, null, null, null, false, null, null, null)],
            1, 3, 3, false, true);
        service.Setup(value => value.GetPaginatedAsync(JobSortType.JobId_Descending, "  Boundary  ", 0, 0, token))
            .ReturnsAsync(page);
        var controller = new JobsController(service.Object);

        var result = await controller.GetPaginatedAsync(JobSortType.JobId_Descending, "  Boundary  ", 0, 0, token);

        Assert.IsType<NotFoundResult>(result.Result);
        Assert.Null(result.Value);
        service.Verify(value => value.GetPaginatedAsync(JobSortType.JobId_Descending, "  Boundary  ", 0, 0, token), Times.Once);
        service.VerifyNoOtherCalls();
    }

    /// <summary>Checks that a zero-size request propagates the original service failure.</summary>
    /// <returns>A task that completes after checking that a zero-size request propagates the original service failure.</returns>
    [Fact]
    public async Task ExplicitZeroSize_DoesNotReplaceServiceFailureWithNotFound()
    {
        var service = new Mock<ICareerService>(MockBehavior.Strict);
        var failure = new InvalidOperationException("Controlled service failure");
        service.Setup(value => value.GetPaginatedAsync(null, null, 1, 0, CancellationToken.None)).ThrowsAsync(failure);
        var controller = new JobsController(service.Object);

        var actual = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            controller.GetPaginatedAsync(null, null, 1, 0, CancellationToken.None));

        Assert.Same(failure, actual);
        service.Verify(value => value.GetPaginatedAsync(null, null, 1, 0, CancellationToken.None), Times.Once);
        service.VerifyNoOtherCalls();
    }

    /// <summary>Checks that a zero-size request preserves caller cancellation.</summary>
    /// <returns>A task that completes after checking that a zero-size request preserves caller cancellation.</returns>
    [Fact]
    public async Task ExplicitZeroSize_DoesNotReplaceCallerCancellationWithNotFound()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var token = cancellation.Token;
        var service = new Mock<ICareerService>(MockBehavior.Strict);
        service.Setup(value => value.GetPaginatedAsync(null, null, 1, 0, token))
            .Returns(Task.FromCanceled<PaginatedJobOfferResponse>(token));
        var controller = new JobsController(service.Object);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            controller.GetPaginatedAsync(null, null, 1, 0, token));

        service.Verify(value => value.GetPaginatedAsync(null, null, 1, 0, token), Times.Once);
        service.VerifyNoOtherCalls();
    }

    private async Task<(int[] All, int[] Matching)> SeedZeroSizeAsync(bool populated, CancellationToken token)
    {
        await fixture.ResetAsync();
        if (!populated) return ([], []);
        await using var db = fixture.Context();
        var level = new JobLevel { Name = "Synthetic zero-size level", Description = "Retained level" };
        db.Offers.AddRange(new[] { "Boundary vacancy A", "Boundary vacancy B", "Other vacancy" }.Select(title => new JobOffer
        {
            Level = level,
            Title = title,
            Description = "Retained description",
            IsFilled = false,
            CreatedDate = new DateTime(2020, 1, 1),
            ModifiedDate = new DateTime(2020, 1, 2),
        }));
        await db.SaveChangesAsync(token);
        var rows = await db.Offers.AsNoTracking().OrderBy(row => row.Id).Select(row => new { row.Id, row.Title }).ToArrayAsync(token);
        return (rows.Select(row => row.Id).ToArray(),
            rows.Where(row => row.Title!.StartsWith("Boundary", StringComparison.Ordinal)).Select(row => row.Id).ToArray());
    }

    private static Task PrimeZeroSizeCacheAsync(IDistributedCache cache, CancellationToken token) => Task.WhenAll(
        cache.SetAsync("jobs:all:v1", System.Text.Encoding.UTF8.GetBytes("retained-career-cache"), token),
        cache.SetAsync("zero-size:unrelated", System.Text.Encoding.UTF8.GetBytes("retained-unrelated-cache"), token));

    private static async Task AssertZeroSizeCacheAsync(IDistributedCache cache, CancellationToken token)
    {
        Assert.Equal(System.Text.Encoding.UTF8.GetBytes("retained-career-cache"), await cache.GetAsync("jobs:all:v1", token));
        Assert.Equal(System.Text.Encoding.UTF8.GetBytes("retained-unrelated-cache"), await cache.GetAsync("zero-size:unrelated", token));
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
                row.Id,
                row.LevelId,
                row.Title,
                row.Introduction,
                row.Description,
                row.Prerequisites,
                row.WhatWeOffer,
                row.Location,
                row.IsFilled,
                row.CreatedDate,
                row.ModifiedDate,
            }).ToArrayAsync(cancellation),
            Levels = await db.Levels.AsNoTracking().OrderBy(row => row.Id).Select(row => new
            {
                row.Id,
                row.Name,
                row.Description,
                row.CreatedDate,
                row.ModifiedDate,
            }).ToArrayAsync(cancellation),
            OfferRevisions = await db.Database.SqlQueryRaw<string>(
                "SELECT \"ID\"::text || ':' || xmin::text AS \"Value\" FROM \"Offer\" ORDER BY \"ID\"").ToArrayAsync(cancellation),
            LevelRevisions = await db.Database.SqlQueryRaw<string>(
                "SELECT \"ID\"::text || ':' || xmin::text AS \"Value\" FROM \"Level\" ORDER BY \"ID\"").ToArrayAsync(cancellation),
        });
    }
}
