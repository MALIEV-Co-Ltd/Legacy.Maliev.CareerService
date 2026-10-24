using Legacy.Maliev.CareerService.Api.Controllers;
using Legacy.Maliev.CareerService.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Moq;

namespace Legacy.Maliev.CareerService.Tests.Controllers;

/// <summary>Checks original null-body controller guards before service calls or record lookup.</summary>
public sealed class CareerOriginalNullControllerTests
{
    /// <summary>Checks that null offer creation returns the original required-message response.</summary>
    /// <returns>A task that completes after checking that null offer creation returns the original required-message response.</returns>
    [Fact]
    public async Task NullOfferCreate_ReturnsOriginalMessageBeforeServiceCalls()
    {
        var service = new Mock<ICareerService>(MockBehavior.Strict);
        var controller = new JobsController(service.Object);
        var result = await controller.CreateOfferAsync(null!, CancellationToken.None);
        AssertNullResult(result, "Offer is required");
        service.VerifyNoOtherCalls();
    }

    /// <summary>Checks that null offer updates fail before looking up the requested identifier.</summary>
    /// <param name="offerId">The offer identifier passed with a null update body to verify rejection before any repository lookup.</param>
    /// <returns>A task that completes after checking that null offer updates fail before looking up the requested identifier.</returns>
    [Theory]
    [InlineData(101)]
    [InlineData(int.MaxValue)]
    public async Task NullOfferUpdate_ReturnsOriginalMessageBeforeLookup(int offerId)
    {
        var service = new Mock<ICareerService>(MockBehavior.Strict);
        var controller = new JobsController(service.Object);
        var result = await controller.UpdateOfferAsync(offerId, null!, CancellationToken.None);
        AssertNullResult(result, "Offer is required");
        service.VerifyNoOtherCalls();
    }

    /// <summary>Checks that null level creation returns the original required-message response.</summary>
    /// <returns>A task that completes after checking that null level creation returns the original required-message response.</returns>
    [Fact]
    public async Task NullLevelCreate_ReturnsOriginalMessageBeforeServiceCalls()
    {
        var service = new Mock<ICareerService>(MockBehavior.Strict);
        var controller = new LevelsController(service.Object);
        var result = await controller.CreateLevelAsync(null!, CancellationToken.None);
        AssertNullResult(result, "Level is required");
        service.VerifyNoOtherCalls();
    }

    /// <summary>Checks that null level updates fail before looking up the requested identifier.</summary>
    /// <param name="levelId">The level identifier passed with a null update body to verify rejection before any repository lookup.</param>
    /// <returns>A task that completes after checking that null level updates fail before looking up the requested identifier.</returns>
    [Theory]
    [InlineData(101)]
    [InlineData(int.MaxValue)]
    public async Task NullLevelUpdate_ReturnsOriginalMessageBeforeLookup(int levelId)
    {
        var service = new Mock<ICareerService>(MockBehavior.Strict);
        var controller = new LevelsController(service.Object);
        var result = await controller.UpdateLevelAsync(levelId, null!, CancellationToken.None);
        AssertNullResult(result, "Level is required");
        service.VerifyNoOtherCalls();
    }

    private static void AssertNullResult(ActionResult result, string message)
    {
        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal(400, badRequest.StatusCode);
        Assert.Equal(message, Assert.IsType<string>(badRequest.Value));
    }
}
