using System.Net;
using System.Text.Json;
using Legacy.Maliev.CareerService.Domain;
using Microsoft.EntityFrameworkCore;

namespace Legacy.Maliev.CareerService.Tests.Controllers;

[Collection("Career shared level")]
public sealed class CareerOfferDetailSourceHttpTests(CareerLifecycleFixture fixture)
    : IClassFixture<CareerLifecycleFixture>
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SourceOfferDetail_LinkedStoredRowOmitsUnloadedLevelAndPreservesWholeGraph(bool nullableFields)
    {
        using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        var cancellation = lifetime.Token;
        await fixture.ResetAsync();
        int offerId;
        int levelId;
        await using (var db = fixture.Context())
        {
            var level = new JobLevel
            {
                Name = "วิศวกรรายละเอียด",
                Description = "Stored detail level",
                CreatedDate = new DateTime(2020, 1, 1),
                ModifiedDate = new DateTime(2020, 1, 2),
            };
            var other = new JobLevel { Name = "Other detail level", Description = "Other detail description" };
            db.Levels.AddRange(level, other);
            await db.SaveChangesAsync(cancellation);
            var offer = new JobOffer
            {
                LevelId = level.Id,
                Title = "  วิศวกรรายละเอียด  ",
                Introduction = nullableFields ? null : "Stored introduction",
                Description = nullableFields ? null : "Stored description",
                Prerequisites = nullableFields ? null : "Stored prerequisites",
                WhatWeOffer = nullableFields ? null : "Stored offer",
                Location = nullableFields ? null : "Thailand",
                IsFilled = nullableFields ? null : false,
                CreatedDate = nullableFields ? null : new DateTime(2020, 2, 1),
                ModifiedDate = nullableFields ? null : new DateTime(2020, 2, 2),
            };
            db.Offers.AddRange(offer, new JobOffer { LevelId = level.Id, Title = "Sibling" },
                new JobOffer { LevelId = other.Id, Title = "Unrelated" });
            await db.SaveChangesAsync(cancellation);
            offerId = offer.Id;
            levelId = level.Id;
            if (nullableFields)
            {
                await db.Offers.Where(row => row.Id == offerId).ExecuteUpdateAsync(
                    setters => setters.SetProperty(row => row.CreatedDate, (DateTime?)null)
                        .SetProperty(row => row.ModifiedDate, (DateTime?)null), cancellation);
            }
        }

        if (nullableFields)
        {
            await using var actual = fixture.Context();
            var stored = await actual.Offers.AsNoTracking().SingleAsync(row => row.Id == offerId, cancellation);
            Assert.Null(stored.Introduction);
            Assert.Null(stored.Description);
            Assert.Null(stored.Prerequisites);
            Assert.Null(stored.WhatWeOffer);
            Assert.Null(stored.Location);
            Assert.Null(stored.IsFilled);
            Assert.Null(stored.CreatedDate);
            Assert.Null(stored.ModifiedDate);
        }

        var before = await WholeGraphAsync(cancellation);
        using var client = fixture.Client();
        using var response = await client.GetAsync($"/Jobs/{offerId}", cancellation);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellation));
        Assert.Equal(before, await WholeGraphAsync(cancellation));
        Assert.Empty(fixture.Authorities);
        var root = json.RootElement;
        Assert.Equal(offerId, root.GetProperty("id").GetInt32());
        Assert.Equal(levelId, root.GetProperty("levelId").GetInt32());
        Assert.Equal("  วิศวกรรายละเอียด  ", root.GetProperty("title").GetString());
        Assert.False(root.TryGetProperty("level", out _));
        var nullableNames = new[] { "introduction", "description", "prerequisites", "whatWeOffer", "location", "isFilled", "createdDate", "modifiedDate" };
        Assert.Equal(
            (nullableFields ? new[] { "id", "levelId", "title" } : new[] { "id", "levelId", "title" }.Concat(nullableNames)).OrderBy(name => name),
            root.EnumerateObject().Select(property => property.Name).OrderBy(name => name));
        if (!nullableFields)
        {
            Assert.Equal("Stored introduction", root.GetProperty("introduction").GetString());
            Assert.Equal("Stored description", root.GetProperty("description").GetString());
            Assert.Equal("Stored prerequisites", root.GetProperty("prerequisites").GetString());
            Assert.Equal("Stored offer", root.GetProperty("whatWeOffer").GetString());
            Assert.Equal("Thailand", root.GetProperty("location").GetString());
            Assert.False(root.GetProperty("isFilled").GetBoolean());
            Assert.Equal(new DateTime(2020, 2, 1), root.GetProperty("createdDate").GetDateTime());
            Assert.Equal(new DateTime(2020, 2, 2), root.GetProperty("modifiedDate").GetDateTime());
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(int.MaxValue)]
    public async Task SourceOfferDetail_MissingIdIs404WithoutMutationOrAuthorityCall(int id)
    {
        using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        var cancellation = lifetime.Token;
        await fixture.ResetAsync();
        await using (var db = fixture.Context())
        {
            var level = new JobLevel { Name = "Surviving detail level", Description = "Surviving detail description" };
            db.Levels.Add(level);
            await db.SaveChangesAsync(cancellation);
            db.Offers.Add(new JobOffer { LevelId = level.Id, Title = "Surviving detail offer" });
            await db.SaveChangesAsync(cancellation);
        }
        var before = await WholeGraphAsync(cancellation);
        using var client = fixture.Client();
        using var response = await client.GetAsync($"/Jobs/{id}", cancellation);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(before, await WholeGraphAsync(cancellation));
        Assert.Empty(fixture.Authorities);
    }

    private async Task<string> WholeGraphAsync(CancellationToken cancellation)
    {
        await using var db = fixture.Context();
        return JsonSerializer.Serialize(new
        {
            Levels = await db.Levels.AsNoTracking().OrderBy(row => row.Id).Select(row => new
            {
                row.Id,
                row.Name,
                row.Description,
                row.CreatedDate,
                row.ModifiedDate,
                Version = EF.Property<uint>(row, "Version"),
            }).ToArrayAsync(cancellation),
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
                Version = EF.Property<uint>(row, "Version"),
            }).ToArrayAsync(cancellation),
        });
    }
}
