using EquityLens.Api.Common;
using EquityLens.Api.Contracts.Research;
using EquityLens.Api.Controllers;
using EquityLens.Api.Services.Ai;
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
        var controller = new ResearchController(
            new FakeDocumentSearchService(),
            new FakeResearchPreflightService(Result<ResearchPreflightResult>.Failure(
                "ticker_not_supported",
                "目前僅支援 0050 成分股。")),
            answerService,
            new FakeResearchRunTraceService(),
            new FakeCurrentUserContext(),
            NullLogger<ResearchController>.Instance);

        var result = await controller.Ask(new ResearchAskRequest("AAPL", "主要風險是什麼？"), CancellationToken.None);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result.Result);
        var error = Assert.IsType<ApiError>(badRequest.Value);
        Assert.Equal("ticker_not_supported", error.Code);
        Assert.False(answerService.WasCalled);
    }

    [Fact]
    public async Task Ask_PreflightSuccess_CallsAnswerService()
    {
        var answerService = new FakeResearchAnswerService();
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
            new FakeCurrentUserContext(),
            NullLogger<ResearchController>.Instance);

        var result = await controller.Ask(new ResearchAskRequest("2330", "主要風險是什麼？"), CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<ResearchAskResponse>(ok.Value);
        Assert.Equal("answer [1]", response.Answer);
        Assert.True(answerService.WasCalled);
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
                []));
        }
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
