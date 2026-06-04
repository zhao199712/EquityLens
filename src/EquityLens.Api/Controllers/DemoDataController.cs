using EquityLens.Api.Contracts.DemoData;
using EquityLens.Api.Services.DemoData;
using Microsoft.AspNetCore.Mvc;

namespace EquityLens.Api.Controllers;

[ApiController]
[Route("api/demo-data")]
public sealed class DemoDataController : ControllerBase
{
    private readonly IDemoDataService _demoDataService;
    private readonly IWebHostEnvironment _environment;

    public DemoDataController(IDemoDataService demoDataService, IWebHostEnvironment environment)
    {
        _demoDataService = demoDataService;
        _environment = environment;
    }

    [HttpGet("status")]
    public async Task<ActionResult<DemoDataStatusResponse>> GetStatus(CancellationToken cancellationToken)
    {
        if (!_environment.IsDevelopment())
        {
            return NotFound();
        }

        return Ok(await _demoDataService.GetStatusAsync(cancellationToken));
    }

    [HttpPost("seed")]
    public async Task<ActionResult<SeedDemoDataResponse>> Seed(CancellationToken cancellationToken)
    {
        if (!_environment.IsDevelopment())
        {
            return NotFound();
        }

        return Ok(await _demoDataService.SeedAsync(cancellationToken));
    }

    [HttpDelete]
    public async Task<ActionResult<ClearDemoDataResponse>> Clear(CancellationToken cancellationToken)
    {
        if (!_environment.IsDevelopment())
        {
            return NotFound();
        }

        return Ok(await _demoDataService.ClearAsync(cancellationToken));
    }
}
