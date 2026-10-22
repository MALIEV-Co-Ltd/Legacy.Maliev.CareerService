using Legacy.Maliev.CareerService.Api.Controllers;
using Legacy.Maliev.CareerService.Application.Interfaces;
using Legacy.Maliev.CareerService.Application.Models;
using Microsoft.AspNetCore.Mvc;

namespace Legacy.Maliev.CareerService.Tests.Controllers;

/// <summary>Checks the controller's original boolean and named creation-route contracts.</summary>
public sealed class JobsControllerCompatibilityTests
{
    /// <summary>Checks that the open-position action returns the service boolean directly.</summary>
    /// <returns>A task that completes after asserting that the controller returns the service open-position boolean directly.</returns>
    [Fact]
    public async Task GetHasOpenPositionsAsync_returns_service_boolean_directly()
    {
        var controller = new JobsController(new StubCareerService(hasOpenPositions: true));

        Assert.True(await controller.GetHasOpenPositionsAsync(CancellationToken.None));
    }

    /// <summary>Checks that offer creation retains the GetOffer route and offerId route value.</summary>
    /// <returns>A task that completes after asserting the created-offer response retains the GetOffer route name and offerId route value.</returns>
    [Fact]
    public async Task CreateOfferAsync_uses_legacy_GetOffer_route_and_offerId_parameter()
    {
        var response = new JobOfferResponse(5, 2, "Engineer", null, null, null, null, null, false, null, null, null);
        var controller = new JobsController(new StubCareerService(createdOffer: response));

        var result = await controller.CreateOfferAsync(new UpsertJobOfferRequest(2, "Engineer", null, null, null, null, null, false), CancellationToken.None);

        var created = Assert.IsType<CreatedAtRouteResult>(result);
        Assert.Equal("GetOffer", created.RouteName);
        Assert.Equal(5, created.RouteValues?["offerId"]);
    }

    private sealed class StubCareerService(bool hasOpenPositions = false, JobOfferResponse? createdOffer = null) : ICareerService
    {
        public Task<PaginatedJobOfferResponse> GetPaginatedAsync(JobSortType? sort, string? search, int? index, int? size, CancellationToken cancellationToken) =>
            Task.FromResult(new PaginatedJobOfferResponse([], 1, 0, 0, false, false));

        public Task<bool> HasOpenPositionsAsync(CancellationToken cancellationToken) => Task.FromResult(hasOpenPositions);

        public Task<JobOfferResponse?> GetOfferByIdAsync(int id, CancellationToken cancellationToken) => Task.FromResult<JobOfferResponse?>(createdOffer);

        public Task<JobOfferResponse> CreateOfferAsync(UpsertJobOfferRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(createdOffer ?? throw new InvalidOperationException("Created response not configured."));

        public Task<bool> UpdateOfferAsync(int id, UpsertJobOfferRequest request, CancellationToken cancellationToken) => Task.FromResult(true);

        public Task<bool> DeleteOfferAsync(int id, CancellationToken cancellationToken) => Task.FromResult(true);

        public Task<IReadOnlyList<JobLevelResponse>> GetLevelsAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<JobLevelResponse>>([]);

        public Task<JobLevelResponse?> GetLevelByIdAsync(int id, CancellationToken cancellationToken) => Task.FromResult<JobLevelResponse?>(null);

        public Task<JobLevelResponse> CreateLevelAsync(UpsertJobLevelRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<bool> UpdateLevelAsync(int id, UpsertJobLevelRequest request, CancellationToken cancellationToken) => Task.FromResult(true);

        public Task<bool> DeleteLevelAsync(int id, CancellationToken cancellationToken) => Task.FromResult(true);
    }
}
