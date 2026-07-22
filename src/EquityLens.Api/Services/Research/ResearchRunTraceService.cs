using System.Text.Json;
using EquityLens.Api.Contracts.Research;
using EquityLens.Api.Data;
using EquityLens.Api.Data.Entities;
using EquityLens.Api.Observability;
using Microsoft.EntityFrameworkCore;

namespace EquityLens.Api.Services.Research;

public sealed class ResearchRunTraceService : IResearchRunTraceService
{
    private readonly EquityLensDbContext _dbContext;
    private readonly ILogger<ResearchRunTraceService> _logger;

    public ResearchRunTraceService(EquityLensDbContext dbContext, ILogger<ResearchRunTraceService> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task<Guid> PersistAskAsync(
        Guid userId,
        ResearchAskRequest request,
        ResearchAskResponse response,
        IReadOnlyList<StepInput>? steps = null,
        CancellationToken cancellationToken = default)
    {
        var run = MapToRun(userId, request, response);
        _dbContext.ResearchRuns.Add(run);

        if (steps is { Count: > 0 })
        {
            foreach (var s in steps)
            {
                run.Steps.Add(MapToStep(run.Id, s));
            }
        }

        if (response.Citations.Count > 0)
        {
            foreach (var c in response.Citations)
            {
                run.Citations.Add(MapToCitation(run.Id, c));
            }
        }

        if (response.Trace?.Results is { Count: > 0 })
        {
            foreach (var t in response.Trace.Results)
            {
                run.Candidates.Add(MapToCandidate(run.Id, t));
            }
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        return run.Id;
    }

    private static ResearchRunStep MapToStep(Guid runId, StepInput s)
    {
        return new ResearchRunStep
        {
            RunId = runId,
            StepType = s.StepType,
            InputJson = s.InputJson,
            OutputJson = s.OutputJson,
            DurationMs = s.DurationMs,
            StartedAtUtc = s.StartedAtUtc,
            CompletedAtUtc = s.CompletedAtUtc,
            ErrorMessage = s.ErrorMessage
        };
    }

    public async Task<IReadOnlyList<ResearchRunSummaryDto>> ListAsync(
        Guid? userId,
        int limit = 50,
        string? ticker = null,
        string? status = null,
        CancellationToken cancellationToken = default)
    {
        var query = _dbContext.ResearchRuns.AsNoTracking();

        if (userId.HasValue)
            query = query.Where(r => r.UserId == userId.Value);
        if (!string.IsNullOrWhiteSpace(ticker))
            query = query.Where(r => r.Ticker == ticker.Trim().ToUpperInvariant());
        if (!string.IsNullOrWhiteSpace(status))
            query = query.Where(r => r.Status == status);

        return await query
            .OrderByDescending(r => r.CreatedAtUtc)
            .Take(limit)
            .Select(r => new ResearchRunSummaryDto(
                r.Id, r.Ticker, r.Question, r.Status, r.CitationCount, r.RetrievalMode, r.LatencyMs, r.CreatedAtUtc, r.ParentResearchRunId, r.RevisionFeedbackId))
            .ToListAsync(cancellationToken);
    }

    public async Task<ResearchRunDetailDto?> GetByIdAsync(Guid id, Guid? userId, CancellationToken cancellationToken = default)
    {
        var runQuery = _dbContext.ResearchRuns.AsNoTracking();
        if (userId.HasValue)
            runQuery = runQuery.Where(r => r.UserId == userId.Value);

        var run = await runQuery.FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
        if (run is null) return null;

        var steps = await _dbContext.ResearchRunSteps
            .AsNoTracking()
            .Where(s => s.RunId == id)
            .OrderBy(s => s.StartedAtUtc)
            .Select(s => new ResearchRunStepDto(
                s.Id, s.StepType, s.InputJson, s.OutputJson, s.DurationMs, s.StartedAtUtc, s.CompletedAtUtc, s.ErrorMessage))
            .ToListAsync(cancellationToken);

        var candidates = await _dbContext.ResearchRunCandidates
            .AsNoTracking()
            .Where(c => c.RunId == id)
            .Select(c => new ResearchRunCandidateDto(
                c.Id, c.DocumentChunkId, c.DocumentId, c.Title, c.DocumentType, c.SourceRole,
                c.PageNumber, c.RelevanceScore, c.AdjustedScore, c.RankBeforeRerank, c.RankAfterRerank,
                c.Decision, c.DiscardReason, c.ContentPreview))
            .ToListAsync(cancellationToken);

        var citations = await _dbContext.ResearchRunCitations
            .AsNoTracking()
            .Where(c => c.RunId == id)
            .OrderBy(c => c.CitationIndex)
            .Select(c => new ResearchRunCitationDto(
                c.Id, c.CitationIndex, c.SourceType, c.DocumentChunkId, c.DocumentId,
                c.Title, c.DocumentType, c.SourceRole, c.PageNumber, c.QuoteText, c.RelevanceScore))
            .ToListAsync(cancellationToken);

        var summary = new ResearchRunSummaryDto(
            run.Id, run.Ticker, run.Question, run.Status, run.CitationCount, run.RetrievalMode, run.LatencyMs, run.CreatedAtUtc, run.ParentResearchRunId, run.RevisionFeedbackId);

        return new ResearchRunDetailDto(summary, run.Answer, steps, candidates, citations);
    }

    private static ResearchRun MapToRun(Guid userId, ResearchAskRequest request, ResearchAskResponse response)
    {
        return new ResearchRun
        {
            UserId = userId,
            TraceId = response.Trace?.TraceId ?? string.Empty,
            Ticker = request.Ticker.Trim().ToUpperInvariant(),
            Question = request.Question,
            Answer = response.Answer,
            Status = response.Status,
            Model = response.Model,
            RetrievalMode = response.RetrievalStrategy.Mode,
            SourcePolicy = request.SourcePolicy.ToString(),
            DocumentType = string.IsNullOrWhiteSpace(request.DocumentType) ? null : request.DocumentType.Trim(),
            TopK = request.TopK,
            Temperature = request.Temperature,
            CitationCount = response.Citations.Count,
            LatencyMs = response.Trace?.LatencyMs.Total ?? 0,
            ErrorMessage = null,
            CreatedAtUtc = DateTime.UtcNow
        };
    }

    private static ResearchRunCitation MapToCitation(Guid runId, ResearchCitation c)
    {
        return new ResearchRunCitation
        {
            RunId = runId,
            CitationIndex = c.Index,
            SourceType = c.SourceType.ToString(),
            DocumentChunkId = c.DocumentChunkId,
            DocumentId = c.DocumentId,
            Title = c.Title,
            DocumentType = c.DocumentType,
            SourceRole = c.SourceRole,
            PageNumber = c.PageNumber,
            QuoteText = c.QuoteText,
            RelevanceScore = c.RelevanceScore
        };
    }

    private static ResearchRunCandidate MapToCandidate(Guid runId, ResearchTraceResult t)
    {
        return new ResearchRunCandidate
        {
            RunId = runId,
            SearchId = t.SearchId,
            Query = t.Query,
            DocumentChunkId = t.DocumentChunkId,
            DocumentId = t.DocumentId,
            Title = t.DocumentTitle,
            DocumentType = t.DocumentType,
            SourceRole = t.SourceRole,
            PageNumber = t.PageNumber,
            RelevanceScore = t.RelevanceScore,
            AdjustedScore = t.AdjustedScore,
            RankBeforeRerank = t.RankBeforeRerank,
            RankAfterRerank = t.RankAfterRerank,
            Decision = t.Decision,
            DiscardReason = t.Reason,
            ContentPreview = t.ContentPreview
        };
    }
}
