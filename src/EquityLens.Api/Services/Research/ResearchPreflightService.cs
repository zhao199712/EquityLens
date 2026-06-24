using EquityLens.Api.Common;
using EquityLens.Api.Contracts.Research;
using EquityLens.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace EquityLens.Api.Services.Research;

public sealed class ResearchPreflightService : IResearchPreflightService
{
    private readonly EquityLensDbContext _dbContext;

    public ResearchPreflightService(EquityLensDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<ResearchPreflightResult>> ValidateAskAsync(
        ResearchAskRequest request,
        CancellationToken cancellationToken = default)
    {
        var ticker = request.Ticker.Trim().ToUpperInvariant();
        var security = await _dbContext.Securities
            .AsNoTracking()
            .Where(s => s.Ticker.ToUpper() == ticker)
            .Select(s => new { s.Id, s.Ticker, s.Exchange, s.Name })
            .FirstOrDefaultAsync(cancellationToken);

        if (security is null)
        {
            return Result<ResearchPreflightResult>.Failure(
                "ticker_not_found",
                "找不到指定股票代號。");
        }

        if (!Tw0050Universe.Contains(security.Ticker))
        {
            return Result<ResearchPreflightResult>.Failure(
                "ticker_not_supported",
                "目前僅支援 0050 成分股。");
        }

        var documentIds = await GetDocumentIdsAsync(security.Id, request.DocumentType, request.RetrievalMode, cancellationToken);
        if (documentIds.Count == 0)
        {
            return Result<ResearchPreflightResult>.Failure(
                "documents_not_found",
                "此股票尚未匯入可供研究問答使用的年報或法說會文件。");
        }

        var chunkCount = await _dbContext.DocumentChunks
            .AsNoTracking()
            .CountAsync(chunk => documentIds.Contains(chunk.DocumentId), cancellationToken);
        if (chunkCount == 0)
        {
            return Result<ResearchPreflightResult>.Failure(
                "chunks_not_found",
                "此股票的研究文件尚未完成切分 chunks。");
        }

        var embeddingCount = await _dbContext.DocumentEmbeddings
            .AsNoTracking()
            .CountAsync(embedding => documentIds.Contains(embedding.DocumentChunk.DocumentId), cancellationToken);
        if (embeddingCount == 0)
        {
            return Result<ResearchPreflightResult>.Failure(
                "embeddings_not_ready",
                "此股票的研究文件尚未完成向量化 embedding。");
        }

        return Result<ResearchPreflightResult>.Success(new ResearchPreflightResult(
            security.Id,
            security.Ticker,
            security.Exchange,
            security.Name,
            documentIds.Count,
            chunkCount,
            embeddingCount));
    }

    private async Task<List<Guid>> GetDocumentIdsAsync(
        Guid securityId,
        string? documentType,
        string? retrievalMode,
        CancellationToken cancellationToken)
    {
        var includeAnnualReports = ShouldIncludeAnnualReports(documentType, retrievalMode);
        var includeConferences = ShouldIncludeConferences(documentType, retrievalMode);
        var documentIds = new List<Guid>();

        if (includeAnnualReports)
        {
            documentIds.AddRange(await _dbContext.FinancialFilings
                .AsNoTracking()
                .Where(filing => filing.SecurityId == securityId && filing.DocumentId != null)
                .Select(filing => filing.DocumentId!.Value)
                .ToListAsync(cancellationToken));
        }

        if (includeConferences)
        {
            documentIds.AddRange(await _dbContext.InvestorConferences
                .AsNoTracking()
                .Where(conference => conference.SecurityId == securityId && conference.DocumentId != null)
                .Select(conference => conference.DocumentId!.Value)
                .ToListAsync(cancellationToken));
        }

        return documentIds.Distinct().ToList();
    }

    private static bool ShouldIncludeAnnualReports(string? documentType, string? retrievalMode)
    {
        if (string.Equals(documentType, "EarningsPresentation", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return !string.Equals(retrievalMode, "ConferenceOnly", StringComparison.OrdinalIgnoreCase);
    }

    private static bool ShouldIncludeConferences(string? documentType, string? retrievalMode)
    {
        if (string.Equals(documentType, "AnnualReport", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return !string.Equals(retrievalMode, "AnnualReportOnly", StringComparison.OrdinalIgnoreCase);
    }
}
