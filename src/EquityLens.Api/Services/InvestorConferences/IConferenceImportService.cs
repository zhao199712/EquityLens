namespace EquityLens.Api.Services.InvestorConferences;

public interface IConferenceImportService
{
    Task<ConferenceImportResult> ImportAllAsync(CancellationToken cancellationToken = default);
}

public record ConferenceImportResult(int Total, int Succeeded, int Failed, int Skipped);
