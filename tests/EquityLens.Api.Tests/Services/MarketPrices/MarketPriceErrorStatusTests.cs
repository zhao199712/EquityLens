using EquityLens.Api.Common;
using EquityLens.Api.Controllers;
using Microsoft.AspNetCore.Mvc;

namespace EquityLens.Api.Tests.Services.MarketPrices;
public sealed class MarketPriceErrorStatusTests
{
    [Theory]
    [InlineData("market_price.quality_error", 422)]
    [InlineData("market_price.provider_error", 502)]
    [InlineData("market_price.update_conflict", 409)]
    [InlineData("security.not_found", 404)]
    [InlineData("market_price.invalid_range", 400)]
    public void PublicFailure_MapsToExpectedHttpStatus(string code, int status)
    {
        var controller = new ProbeController();
        Assert.Equal(status, Assert.IsAssignableFrom<ObjectResult>(controller.Map(code).Result).StatusCode);
    }
    private sealed class ProbeController : ApiControllerBase
    {
        public ActionResult<string> Map(string code) => ToActionResult(Result<string>.Failure(code, "fixture"));
    }
}
