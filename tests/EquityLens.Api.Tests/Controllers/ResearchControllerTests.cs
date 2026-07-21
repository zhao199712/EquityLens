using EquityLens.Api.Common;
using EquityLens.Api.Contracts.Research;
using EquityLens.Api.Contracts.Agents;
using EquityLens.Api.Controllers;
using EquityLens.Api.Services.Ai;
using EquityLens.Api.Services.Agents;
using EquityLens.Api.Services.CurrentUser;
using EquityLens.Api.Services.Documents;
using EquityLens.Api.Services.Research;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;

namespace EquityLens.Api.Tests.Controllers;

public sealed class ResearchControllerTests
{
    [Fact]
    public async Task Ask_PreflightFailure_ReturnsBadRequestAndDoesNotCallAnswerService()
    {
        var answerService = new FakeResearchAnswerService();
        var agentRunService = new FakeAgentRunService();
        var controller = new ResearchController(
            new FakeDocumentSearchService(),
            new FakeResearchPreflightService(Result<ResearchPreflightResult>.Failure(
                "ticker_not_supported",
                "目前僅支援 0050 成分股。")),
            answerService,
            new FakeResearchRunTraceService(),
            agentRunService,
            new FakeCurrentUserContext(),
            NullLogger<ResearchController>.Instance);

        var result = await controller.Ask(new ResearchAskRequest("AAPL", "主要風險是什麼？"), CancellationToken.None);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result.Result);
        var error = Assert.IsType<ApiError>(badRequest.Value);
        Assert.Equal("ticker_not_supported", error.Code);
        Assert.False(answerService.WasCalled);
        Assert.False(agentRunService.WasCalled);
    }

    [Fact]
    public async Task Ask_PreflightSuccess_CallsAnswerService()
    {
        var researchRunId = Guid.NewGuid();
        var answerService = new FakeResearchAnswerService(researchRunId);
        var agentRunService = new FakeAgentRunService();
        var controller = new ResearchController(
            new FakeDocumentSearchService(),
            new FakeResearchPreflightService(Result<ResearchPreflightResult>.Success(new ResearchPreflightResult(
                Guid.NewGuid(),
                "2330",
                "TWSE",
                "台積電",
                1,
                3,
                3))),
            answerService,
            new FakeResearchRunTraceService(),
            agentRunService,
            new FakeCurrentUserContext(),
            NullLogger<ResearchController>.Instance);

        var result = await controller.Ask(new ResearchAskRequest("2330", "主要風險是什麼？"), CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<ResearchAskResponse>(ok.Value);
        Assert.Equal("answer [1]", response.Answer);
        Assert.True(answerService.WasCalled);
        Assert.False(agentRunService.WasCalled);
    }

    [Fact]
    public async Task CreateInvestigation_ValidRequest_ReturnsAcceptedAndStartsWorkflow()
    {
        var agentRunService = new FakeAgentRunService();
        var controller = new ResearchController(
            new FakeDocumentSearchService(),
            new FakeResearchPreflightService(Result<ResearchPreflightResult>.Failure("unused", "unused")),
            new FakeResearchAnswerService(),
            new FakeResearchRunTraceService(),
            agentRunService,
            new FakeCurrentUserContext(),
            NullLogger<ResearchController>.Instance);

        var result = await controller.CreateInvestigation(new ResearchAskRequest("2330", "主要風險是什麼？"), CancellationToken.None);

        var accepted = Assert.IsType<AcceptedResult>(result.Result);
        var response = Assert.IsType<ResearchInvestigationCreatedResponse>(accepted.Value);
        Assert.Equal(AgentWorkflowTypes.ResearchInvestigation, response.WorkflowType);
        Assert.Equal(AgentRunStatuses.Pending, response.Status);
        Assert.True(agentRunService.WasCalled);
    }

    [Theory]
    [InlineData("2330", true)]
    [InlineData("AAPL", false)]
    public void Tw0050Universe_Contains_ReturnsExpectedResult(string ticker, bool expected)
    {
        Assert.Equal(expected, Tw0050Universe.Contains(ticker));
    }

    private sealed class FakeResearchPreflightService : IResearchPreflightService
    {
        private readonly Result<ResearchPreflightResult> _result;

        public FakeResearchPreflightService(Result<ResearchPreflightResult> result)
        {
            _result = result;
        }

        public Task<Result<ResearchPreflightResult>> ValidateAskAsync(
            ResearchAskRequest request,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_result);
        }
    }

    private sealed class FakeResearchAnswerService : IResearchAnswerService
    {
        private readonly Guid? _researchRunId;

        public FakeResearchAnswerService(Guid? researchRunId = null) => _researchRunId = researchRunId;

        public bool WasCalled { get; private set; }

        public Task<ResearchAskResponse> AskAsync(
            ResearchAskRequest request,
            CancellationToken cancellationToken = default)
        {
            WasCalled = true;
            return Task.FromResult(new ResearchAskResponse(
                request.Question,
                "answer [1]",
                "test-model",
                new ResearchRetrievalStrategy("Auto", []),
                [],
                ResearchRunId: _researchRunId));
        }
    }

    private sealed class FakeAgentRunService : IAgentRunService
    {
        public bool WasCalled { get; private set; }
        public Task<(AgentRunSummaryResponse AgentRun, Guid ResearchRunId)> CreateResearchInvestigationAsync(Guid userId, ResearchAskRequest request, CancellationToken cancellationToken = default)
        {
            WasCalled = true;
            var researchRunId = Guid.NewGuid();
            return Task.FromResult((new AgentRunSummaryResponse(Guid.NewGuid(), AgentWorkflowTypes.ResearchInvestigation, AgentTypes.Research, AgentRunStatuses.Pending, DateTime.UtcNow, null, null, null, 0, 0, 0), researchRunId));
        }
        public Task<AgentRunSummaryResponse> CreateCriticReviewAsync(Guid userId, Guid researchRunId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<AgentRunSummaryResponse> CreateDraftRevisionAsync(Guid userId, Guid criticReviewRunId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<AgentRunSummaryResponse> CreateResearchQualityReviewAsync(Guid userId, Guid researchRunId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<AgentRunSummaryResponse> CreateEvidenceRemediationAsync(Guid userId, Guid criticReviewRunId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<AgentRunSummaryResponse> CreateEvidenceReanalysisAsync(Guid userId, Guid evidenceRemediationRunId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<AgentRunSummaryResponse> CreatePortfolioDiagnosisAsync(Guid userId, Guid portfolioId, DateOnly? from, DateOnly? to, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<AgentRunSummaryResponse>> ListAsync(Guid? userId, int limit = 50, string? workflowType = null, string? status = null, Guid? researchRunId = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<AgentRunDetailResponse?> GetByIdAsync(Guid id, Guid? userId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<AgentRunSummaryResponse?> RetryAsync(Guid id, Guid userId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<AgentRunSummaryResponse?> CancelAsync(Guid id, Guid userId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    }

    private sealed class FakeDocumentSearchService : IDocumentSearchService
    {
        public Task<DocumentSearchResponse> SearchAsync(
            DocumentSearchRequest request,
            CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }
    }

    private sealed class FakeResearchRunTraceService : IResearchRunTraceService
    {
        public Task<Guid> PersistAskAsync(Guid userId, ResearchAskRequest request, ResearchAskResponse response, IReadOnlyList<StepInput>? steps = null, CancellationToken cancellationToken = default)
            => Task.FromResult(Guid.Empty);

        public Task<IReadOnlyList<ResearchRunSummaryDto>> ListAsync(Guid? userId, int limit = 50, string? ticker = null, string? status = null, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<ResearchRunSummaryDto>>([]);

        public Task<ResearchRunDetailDto?> GetByIdAsync(Guid id, Guid? userId, CancellationToken cancellationToken = default)
            => Task.FromResult<ResearchRunDetailDto?>(null);
    }

    private sealed class FakeCurrentUserContext : ICurrentUserContext
    {
        public Guid UserId => Guid.NewGuid();
        public string Email => "test@example.test";
        public string DisplayName => "Test User";
        public bool IsAuthenticated => true;
    }
}
