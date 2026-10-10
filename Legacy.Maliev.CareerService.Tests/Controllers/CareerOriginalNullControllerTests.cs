using Legacy.Maliev.CareerService.Api.Controllers;
using Legacy.Maliev.CareerService.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Moq;

namespace Legacy.Maliev.CareerService.Tests.Controllers;

public sealed class CareerOriginalNullControllerTests
{
    [Fact]
    public async Task NullOfferCreate_ReturnsOriginalMessageBeforeServiceCalls()
    {
        var service = new Mock<ICareerService>(MockBehavior.Strict);
        var controller = new JobsController(service.Object);
        var result = await controller.CreateOfferAsync(null!, CancellationToken.None);
        AssertNullResult(result, "Offer is required");
        service.VerifyNoOtherCalls();
    }

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

    [Fact]
    public async Task NullLevelCreate_ReturnsOriginalMessageBeforeServiceCalls()
    {
        var service = new Mock<ICareerService>(MockBehavior.Strict);
        var controller = new LevelsController(service.Object);
        var result = await controller.CreateLevelAsync(null!, CancellationToken.None);
        AssertNullResult(result, "Level is required");
        service.VerifyNoOtherCalls();
    }

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
