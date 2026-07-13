using EquityLens.Api.Common;
using EquityLens.Api.Contracts.Agents;
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

    private sealed class FakeAgentRunService : IAgentRunService
    {
        public bool CreateWasCalled { get; private set; }
        public bool CreateDraftRevisionWasCalled { get; private set; }
        public bool CreateResearchQualityReviewWasCalled { get; private set; }
        public Guid LastResearchRunId { get; private set; }
        public Guid LastCriticReviewRunId { get; private set; }
        public AgentRunDetailResponse? Detail { get; set; } = new(
            new AgentRunSummaryResponse(Guid.NewGuid(), "CriticReview", "CriticAgent", "Succeeded", DateTime.UtcNow, DateTime.UtcNow, DateTime.UtcNow, null),
            [], [], [], [], "{}", "{}", "{}");

        public Task<AgentRunSummaryResponse> CreateCriticReviewAsync(Guid userId, Guid researchRunId, CancellationToken cancellationToken = default)
        {
            CreateWasCalled = true;
            LastResearchRunId = researchRunId;
            return Task.FromResult(new AgentRunSummaryResponse(
                Guid.NewGuid(), "CriticReview", "CriticAgent", "Succeeded", DateTime.UtcNow, DateTime.UtcNow, DateTime.UtcNow, null));
        }

        public Task<AgentRunSummaryResponse> CreateDraftRevisionAsync(Guid userId, Guid criticReviewRunId, CancellationToken cancellationToken = default)
        {
            CreateDraftRevisionWasCalled = true;
            LastCriticReviewRunId = criticReviewRunId;
            return Task.FromResult(new AgentRunSummaryResponse(
                Guid.NewGuid(), "DraftRevision", "DraftAgent", "Succeeded", DateTime.UtcNow, DateTime.UtcNow, DateTime.UtcNow, null));
        }

        public Task<AgentRunSummaryResponse> CreateResearchQualityReviewAsync(Guid userId, Guid researchRunId, CancellationToken cancellationToken = default)
        {
            CreateResearchQualityReviewWasCalled = true;
            LastResearchRunId = researchRunId;
            return Task.FromResult(new AgentRunSummaryResponse(
                Guid.NewGuid(), "ResearchQualityReview", "CriticAgent", "Succeeded", DateTime.UtcNow, DateTime.UtcNow, DateTime.UtcNow, null));
        }

        public Task<IReadOnlyList<AgentRunSummaryResponse>> ListAsync(Guid? userId, int limit = 50, string? workflowType = null, string? status = null, Guid? researchRunId = null, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<AgentRunSummaryResponse>>([]);

        public Task<AgentRunDetailResponse?> GetByIdAsync(Guid id, Guid? userId, CancellationToken cancellationToken = default)
            => Task.FromResult(Detail);

        public Task<AgentRunSummaryResponse?> RetryAsync(Guid id, Guid userId, CancellationToken cancellationToken = default)
            => Task.FromResult<AgentRunSummaryResponse?>(null);

        public Task<AgentRunSummaryResponse?> CancelAsync(Guid id, Guid userId, CancellationToken cancellationToken = default)
            => Task.FromResult<AgentRunSummaryResponse?>(null);
    }

    private sealed class FakeCurrentUserContext : ICurrentUserContext
    {
        public Guid UserId => Guid.Parse("11111111-1111-1111-1111-111111111111");
        public string Email => "test@example.test";
        public string DisplayName => "Test User";
        public bool IsAuthenticated => true;
    }
}
