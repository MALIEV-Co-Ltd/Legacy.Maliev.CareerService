using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Legacy.Maliev.CareerService.Domain;
using Microsoft.EntityFrameworkCore;

namespace Legacy.Maliev.CareerService.Tests.Controllers;

[Collection("Career shared level")]
public sealed class CareerLevelOffersSourceHttpTests(CareerLifecycleFixture fixture)
    : IClassFixture<CareerLifecycleFixture>
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AnonymousLevelRead_PreservesOriginalEmptyOffersCollection(bool detail)
    {
        using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        var cancellation = lifetime.Token;
        await fixture.ResetAsync();
        int id;
        await using (var db = fixture.Context())
        {
            var level = new JobLevel
            {
                Name = "วิศวกร",
                Description = "Synthetic original level navigation",
                CreatedDate = new DateTime(2020, 1, 1),
                ModifiedDate = new DateTime(2020, 1, 2),
            };
            db.Levels.Add(level);
            await db.SaveChangesAsync(cancellation);
            id = level.Id;
        }

        var before = await SnapshotAsync(cancellation);
        using var client = fixture.Client();
        using var response = await client.GetAsync(detail ? $"/jobs/Levels/{id}" : "/jobs/Levels", cancellation);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellation));
        Assert.Equal(before, await SnapshotAsync(cancellation));
        var levelJson = detail ? json.RootElement : Assert.Single(json.RootElement.EnumerateArray().ToArray());
        Assert.Equal(id, levelJson.GetProperty("id").GetInt32());
        Assert.Equal("วิศวกร", levelJson.GetProperty("name").GetString());
        Assert.Equal("Synthetic original level navigation", levelJson.GetProperty("description").GetString());
        Assert.Equal(new DateTime(2020, 1, 1), levelJson.GetProperty("createdDate").GetDateTime());
        Assert.Equal(new DateTime(2020, 1, 2), levelJson.GetProperty("modifiedDate").GetDateTime());
        Assert.Empty(fixture.Authorities);
        AssertOriginalEmptyOffers(levelJson);
    }

    [Fact]
    public async Task AuthorizedLevelCreate_PreservesOriginalEmptyOffersCollection()
    {
        using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        var cancellation = lifetime.Token;
        await fixture.ResetAsync();
        using var client = fixture.Client("legacy-career.levels.create");
        using var response = await client.PostAsJsonAsync("/jobs/Levels", new
        {
            name = "วิศวกร",
            description = "Synthetic original created level",
        }, cancellation);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellation));
        var levelJson = json.RootElement;
        var id = levelJson.GetProperty("id").GetInt32();
        Assert.True(id > 0);
        Assert.EndsWith($"/jobs/Levels/{id}", response.Headers.Location!.ToString(), StringComparison.OrdinalIgnoreCase);
        var authority = Assert.Single(fixture.Authorities.Values);
        Assert.Equal("legacy-career.levels.create", authority.Permission);
        Assert.True(authority.Calls > 0);
        await using (var db = fixture.Context())
        {
            var stored = Assert.Single(await db.Levels.AsNoTracking().ToArrayAsync(cancellation));
            Assert.Equal(id, stored.Id);
            Assert.Equal("วิศวกร", stored.Name);
            Assert.Equal("Synthetic original created level", stored.Description);
            Assert.Equal(stored.Name, levelJson.GetProperty("name").GetString());
            Assert.Equal(stored.Description, levelJson.GetProperty("description").GetString());
            Assert.NotNull(stored.CreatedDate);
            Assert.NotNull(stored.ModifiedDate);
            var createdTicks = levelJson.GetProperty("createdDate").GetDateTime().Ticks;
            var modifiedTicks = levelJson.GetProperty("modifiedDate").GetDateTime().Ticks;
            Assert.Equal(createdTicks - createdTicks % 10, stored.CreatedDate.Value.Ticks);
            Assert.Equal(modifiedTicks - modifiedTicks % 10, stored.ModifiedDate.Value.Ticks);
            Assert.Empty(await db.Offers.AsNoTracking().ToArrayAsync(cancellation));
        }
        AssertOriginalEmptyOffers(levelJson);
    }

    private static void AssertOriginalEmptyOffers(JsonElement level)
    {
        Assert.True(level.TryGetProperty("offers", out var offers),
            "Original Level exposes its public initialized Offers collection.");
        Assert.Equal(JsonValueKind.Array, offers.ValueKind);
        Assert.Equal(0, offers.GetArrayLength());
    }

    private async Task<string> SnapshotAsync(CancellationToken cancellation)
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
            OfferCount = await db.Offers.CountAsync(cancellation),
        });
    }
}
