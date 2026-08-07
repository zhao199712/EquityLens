using EquityLens.Api.Common;
using EquityLens.Api.Contracts.Agents;
using EquityLens.Api.Contracts.Research;
using EquityLens.Api.Controllers;
using EquityLens.Api.Services.Agents;
using EquityLens.Api.Services.CurrentUser;
using Microsoft.AspNetCore.Mvc;

namespace EquityLens.Api.Tests.Controllers;

public sealed class AgentRunsControllerTests
{
    [Fact]
    public async Task CreateCriticReview_EmptyResearchRunId_ReturnsBadRequest()
    {
        var service = new FakeAgentRunService();
        var controller = new AgentRunsController(service, new FakeCurrentUserContext());

        var result = await controller.CreateCriticReview(new CreateCriticReviewRequest(Guid.Empty), CancellationToken.None);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result.Result);
        var error = Assert.IsType<ApiError>(badRequest.Value);
        Assert.Equal("research_run_id_required", error.Code);
        Assert.False(service.CreateWasCalled);
    }

    [Fact]
    public async Task CreateCriticReview_ValidRequest_CallsService()
    {
        var service = new FakeAgentRunService();
        var controller = new AgentRunsController(service, new FakeCurrentUserContext());
        var researchRunId = Guid.NewGuid();

        var result = await controller.CreateCriticReview(new CreateCriticReviewRequest(researchRunId), CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<AgentRunSummaryResponse>(ok.Value);
        Assert.Equal("CriticReview", response.WorkflowType);
        Assert.Equal(researchRunId, service.LastResearchRunId);
        Assert.True(service.CreateWasCalled);
    }

    [Fact]
    public async Task CreateDraftRevision_EmptyCriticReviewRunId_ReturnsBadRequest()
    {
        var service = new FakeAgentRunService();
        var controller = new AgentRunsController(service, new FakeCurrentUserContext());

        var result = await controller.CreateDraftRevision(new CreateDraftRevisionRequest(Guid.Empty), CancellationToken.None);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result.Result);
        var error = Assert.IsType<ApiError>(badRequest.Value);
        Assert.Equal("critic_review_run_id_required", error.Code);
        Assert.False(service.CreateDraftRevisionWasCalled);
    }

    [Fact]
    public async Task CreateDraftRevision_ValidRequest_CallsService()
    {
        var service = new FakeAgentRunService();
        var controller = new AgentRunsController(service, new FakeCurrentUserContext());
        var criticReviewRunId = Guid.NewGuid();

        var result = await controller.CreateDraftRevision(new CreateDraftRevisionRequest(criticReviewRunId), CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<AgentRunSummaryResponse>(ok.Value);
        Assert.Equal("DraftRevision", response.WorkflowType);
        Assert.Equal(criticReviewRunId, service.LastCriticReviewRunId);
        Assert.True(service.CreateDraftRevisionWasCalled);
    }

    [Fact]
    public async Task GetDetail_NotFound_ReturnsNotFound()
    {
        var service = new FakeAgentRunService { Detail = null };
        var controller = new AgentRunsController(service, new FakeCurrentUserContext());

        var result = await controller.GetDetail(Guid.NewGuid(), CancellationToken.None);

        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public async Task CreateResearchQualityReview_EmptyResearchRunId_ReturnsBadRequest()
    {
        var service = new FakeAgentRunService();
        var controller = new AgentRunsController(service, new FakeCurrentUserContext());

        var result = await controller.CreateResearchQualityReview(new CreateResearchQualityReviewRequest(Guid.Empty), CancellationToken.None);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result.Result);
        var error = Assert.IsType<ApiError>(badRequest.Value);
        Assert.Equal("research_run_id_required", error.Code);
        Assert.False(service.CreateResearchQualityReviewWasCalled);
    }

    [Fact]
    public async Task CreateResearchQualityReview_ValidRequest_CallsService()
    {
        var service = new FakeAgentRunService();
        var controller = new AgentRunsController(service, new FakeCurrentUserContext());
        var researchRunId = Guid.NewGuid();

        var result = await controller.CreateResearchQualityReview(new CreateResearchQualityReviewRequest(researchRunId), CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<AgentRunSummaryResponse>(ok.Value);
        Assert.Equal("ResearchQualityReview", response.WorkflowType);
        Assert.Equal(researchRunId, service.LastResearchRunId);
        Assert.True(service.CreateResearchQualityReviewWasCalled);
    }

    [Fact]
    public async Task CreateEvidenceRemediation_ValidRequest_CallsService()
    {
        var service = new FakeAgentRunService(); var controller = new AgentRunsController(service, new FakeCurrentUserContext()); var criticRunId = Guid.NewGuid();
        var result = await controller.CreateEvidenceRemediation(new CreateEvidenceRemediationRequest(criticRunId), CancellationToken.None);
        var ok = Assert.IsType<OkObjectResult>(result.Result); var response = Assert.IsType<AgentRunSummaryResponse>(ok.Value);
        Assert.Equal(AgentWorkflowTypes.EvidenceRemediation, response.WorkflowType); Assert.Equal(criticRunId, service.LastCriticReviewRunId); Assert.True(service.CreateEvidenceRemediationWasCalled);
    }

    [Fact]
    public async Task CreateEvidenceReanalysis_ValidRequest_CallsService()
    {
        var service = new FakeAgentRunService(); var controller = new AgentRunsController(service, new FakeCurrentUserContext()); var sourceRunId = Guid.NewGuid();
        var result = await controller.CreateEvidenceReanalysis(new CreateEvidenceReanalysisRequest(sourceRunId), CancellationToken.None);
        var ok = Assert.IsType<OkObjectResult>(result.Result); var response = Assert.IsType<AgentRunSummaryResponse>(ok.Value);
        Assert.Equal(AgentWorkflowTypes.EvidenceReanalysis, response.WorkflowType); Assert.Equal(sourceRunId, service.LastEvidenceRemediationRunId);
    }

    [Fact]
    public async Task Approve_ValidRequest_CallsApprovalService()
    {
        var runId = Guid.NewGuid(); var approvalId = Guid.NewGuid(); var requestId = Guid.NewGuid();
        var approval = new FakeAgentApprovalService
        {
            Response = BuildApproval(approvalId, runId, AgentApprovalStatuses.Approved, requestId)
        };
        var controller = new AgentRunsController(new FakeAgentRunService(), new FakeCurrentUserContext(), approval);

        var result = await controller.Approve(runId, approvalId, new DecideAgentApprovalRequest(requestId, "看起來沒問題"), CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result); var response = Assert.IsType<AgentApprovalResponse>(ok.Value);
        Assert.Equal(approvalId, response.Id); Assert.True(approval.ApproveWasCalled); Assert.Equal("看起來沒問題", approval.LastRequest?.Comment);
    }

    [Fact]
    public async Task Approve_RunNotFound_ReturnsNotFound()
    {
        var controller = new AgentRunsController(new FakeAgentRunService(), new FakeCurrentUserContext(), new FakeAgentApprovalService());
        var result = await controller.Approve(Guid.NewGuid(), Guid.NewGuid(), new DecideAgentApprovalRequest(Guid.NewGuid()), CancellationToken.None);
        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public async Task Reject_AlreadyDecided_ReturnsConflict()
    {
        var approval = new FakeAgentApprovalService { Exception = new AgentApprovalException("approval_already_decided", "Already decided.") };
        var controller = new AgentRunsController(new FakeAgentRunService(), new FakeCurrentUserContext(), approval);
        var result = await controller.Reject(Guid.NewGuid(), Guid.NewGuid(), new DecideAgentApprovalRequest(Guid.NewGuid(), "拒絕"), CancellationToken.None);
        var conflict = Assert.IsType<ConflictObjectResult>(result.Result);
        Assert.Equal("approval_already_decided", Assert.IsType<ApiError>(conflict.Value).Code);
    }

    private static AgentApprovalResponse BuildApproval(Guid approvalId, Guid runId, string status, Guid requestId) =>
        new(approvalId, runId, Guid.NewGuid(), "node", "NodeType", status, "ExternalWrite", "reason",
            DateTime.UtcNow, DateTime.UtcNow, null, Guid.NewGuid(), null, requestId);

    private sealed class FakeAgentApprovalService : IAgentApprovalService
    {
        public AgentApprovalResponse? Response { get; init; }
        public AgentApprovalException? Exception { get; init; }
        public bool ApproveWasCalled { get; private set; }
        public DecideAgentApprovalRequest? LastRequest { get; private set; }

        public Task<AgentApprovalResponse?> ApproveAsync(Guid runId, Guid approvalId, Guid actorUserId, bool isAdmin, DecideAgentApprovalRequest request, CancellationToken cancellationToken = default)
        {
            if (Exception is not null) throw Exception;
            ApproveWasCalled = true; LastRequest = request; return Task.FromResult(Response);
        }

        public Task<AgentApprovalResponse?> RejectAsync(Guid runId, Guid approvalId, Guid actorUserId, bool isAdmin, DecideAgentApprovalRequest request, CancellationToken cancellationToken = default)
        {
            if (Exception is not null) throw Exception;
            LastRequest = request; return Task.FromResult(Response);
        }
    }

    private sealed class FakeAgentRunService : IAgentRunService
    {
        public Task<(AgentRunSummaryResponse AgentRun, Guid ResearchRunId)> CreateResearchInvestigationAsync(Guid userId, ResearchAskRequest request, InvestmentResearchRoutingContext? routingContext = null, CancellationToken cancellationToken = default)
        {
            var researchRunId = Guid.NewGuid();
            return Task.FromResult((new AgentRunSummaryResponse(Guid.NewGuid(), AgentWorkflowTypes.ResearchInvestigation, AgentTypes.Research, AgentRunStatuses.Pending, DateTime.UtcNow, null, null, null, 0, 0, 0), researchRunId));
        }
        public bool CreateWasCalled { get; private set; }
        public bool CreateDraftRevisionWasCalled { get; private set; }
        public bool CreateResearchQualityReviewWasCalled { get; private set; }
        public bool CreateEvidenceRemediationWasCalled { get; private set; }
        public Guid LastResearchRunId { get; private set; }
        public Guid LastCriticReviewRunId { get; private set; }
        public Guid LastEvidenceRemediationRunId { get; private set; }
        public AgentRunDetailResponse? Detail { get; set; } = new(
            new AgentRunSummaryResponse(Guid.NewGuid(), "CriticReview", "CriticAgent", "Succeeded", DateTime.UtcNow, DateTime.UtcNow, DateTime.UtcNow, null, 0, 0, 0m),
            [], [], [], [], "{}", "{}", "{}");

        public Task<AgentRunSummaryResponse> CreateCriticReviewAsync(Guid userId, Guid researchRunId, CancellationToken cancellationToken = default)
        {
            CreateWasCalled = true;
            LastResearchRunId = researchRunId;
            return Task.FromResult(new AgentRunSummaryResponse(
                Guid.NewGuid(), "CriticReview", "CriticAgent", "Succeeded", DateTime.UtcNow, DateTime.UtcNow, DateTime.UtcNow, null, 0, 0, 0m));
        }

        public Task<AgentRunSummaryResponse> CreateDraftRevisionAsync(Guid userId, Guid criticReviewRunId, CancellationToken cancellationToken = default)
        {
            CreateDraftRevisionWasCalled = true;
            LastCriticReviewRunId = criticReviewRunId;
            return Task.FromResult(new AgentRunSummaryResponse(
                Guid.NewGuid(), "DraftRevision", "DraftAgent", "Succeeded", DateTime.UtcNow, DateTime.UtcNow, DateTime.UtcNow, null, 0, 0, 0m));
        }

        public Task<AgentRunSummaryResponse> CreateResearchQualityReviewAsync(Guid userId, Guid researchRunId, CancellationToken cancellationToken = default)
        {
            CreateResearchQualityReviewWasCalled = true;
            LastResearchRunId = researchRunId;
            return Task.FromResult(new AgentRunSummaryResponse(
                Guid.NewGuid(), "ResearchQualityReview", "CriticAgent", "Succeeded", DateTime.UtcNow, DateTime.UtcNow, DateTime.UtcNow, null, 0, 0, 0m));
        }

        public Task<AgentRunSummaryResponse> CreateEvidenceRemediationAsync(Guid userId, Guid criticReviewRunId, CancellationToken cancellationToken = default)
        {
            CreateEvidenceRemediationWasCalled = true; LastCriticReviewRunId = criticReviewRunId;
            return Task.FromResult(new AgentRunSummaryResponse(Guid.NewGuid(), AgentWorkflowTypes.EvidenceRemediation, AgentTypes.Research, AgentRunStatuses.Pending, DateTime.UtcNow, null, null, null, 0, 0, 0m));
        }

        public Task<AgentRunSummaryResponse> CreateEvidenceReanalysisAsync(Guid userId, Guid evidenceRemediationRunId, CancellationToken cancellationToken = default)
        {
            LastEvidenceRemediationRunId = evidenceRemediationRunId;
            return Task.FromResult(new AgentRunSummaryResponse(Guid.NewGuid(), AgentWorkflowTypes.EvidenceReanalysis, AgentTypes.Analysis, AgentRunStatuses.Pending, DateTime.UtcNow, null, null, null, 0, 0, 0m));
        }

        public Task<AgentRunSummaryResponse> CreatePortfolioDiagnosisAsync(Guid userId, Guid portfolioId, DateOnly? from, DateOnly? to, InvestmentResearchRoutingContext? routingContext = null, CancellationToken cancellationToken = default)
            => Task.FromResult(new AgentRunSummaryResponse(
                Guid.NewGuid(), "PortfolioDiagnosis", "PortfolioDiagnosisAgent", "Pending", DateTime.UtcNow, null, null, null, 0, 0, 0m));

        public Task<SubmitAgentFeedbackResponse> SubmitFeedbackAsync(Guid runId, Guid userId, SubmitAgentFeedbackRequest request, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public Task<IReadOnlyList<AgentRunSummaryResponse>> ListChildrenAsync(Guid parentRunId, Guid userId, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<AgentRunSummaryResponse>>([]);

        public Task<IReadOnlyList<AgentRunSummaryResponse>> ListAsync(Guid? userId, int limit = 50, string? workflowType = null, string? status = null, Guid? researchRunId = null, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<AgentRunSummaryResponse>>([]);

        public Task<AgentRunDetailResponse?> GetByIdAsync(Guid id, Guid? userId, CancellationToken cancellationToken = default)
            => Task.FromResult(Detail);

        public Task<AgentRunSummaryResponse?> RetryAsync(Guid id, Guid userId, CancellationToken cancellationToken = default)
            => Task.FromResult<AgentRunSummaryResponse?>(null);

        public Task<AgentRunSummaryResponse?> CancelAsync(Guid id, Guid userId, CancellationToken cancellationToken = default)
            => Task.FromResult<AgentRunSummaryResponse?>(null);

        public bool DecideApprovalWasCalled { get; private set; }
        public Guid LastApprovalRunId { get; private set; }
        public string? LastApprovalDecision { get; private set; }
        public string? LastApprovalComment { get; private set; }
        public AgentApprovalException? ApprovalException { get; set; }

        public Task<AgentRunSummaryResponse> DecideApprovalAsync(Guid runId, Guid userId, string decision, string? comment, CancellationToken cancellationToken = default)
        {
            if (ApprovalException is not null) throw ApprovalException;
            DecideApprovalWasCalled = true;
            LastApprovalRunId = runId;
            LastApprovalDecision = decision;
            LastApprovalComment = comment;
            return Task.FromResult(new AgentRunSummaryResponse(runId, "HumanApprovalTest", AgentTypes.Analysis, AgentRunStatuses.Running, DateTime.UtcNow, DateTime.UtcNow, null, null, 0, 0, 0m));
        }
    }

    private sealed class FakeCurrentUserContext : ICurrentUserContext
    {
        public Guid UserId => Guid.Parse("11111111-1111-1111-1111-111111111111");
        public string Email => "test@example.test";
        public string DisplayName => "Test User";
        public bool IsAuthenticated => true;
    }
}
