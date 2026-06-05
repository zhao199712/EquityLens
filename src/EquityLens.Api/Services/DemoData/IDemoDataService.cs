using EquityLens.Api.Contracts.DemoData;

namespace EquityLens.Api.Services.DemoData;

public interface IDemoDataService
{
    Task<DemoDataStatusResponse> GetStatusAsync(CancellationToken cancellationToken);
    Task<SeedDemoDataResponse> SeedAsync(CancellationToken cancellationToken);
    Task<ClearDemoDataResponse> ClearAsync(CancellationToken cancellationToken);
}
