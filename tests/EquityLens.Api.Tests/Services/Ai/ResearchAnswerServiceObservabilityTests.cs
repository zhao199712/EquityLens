using System.Collections.Concurrent;
using System.Diagnostics;
using EquityLens.Api.Contracts.Research;
using EquityLens.Api.Observability;
using EquityLens.Api.Services.Ai;
using EquityLens.Api.Services.Ai.Retrieval;
using EquityLens.Api.Services.Documents;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace EquityLens.Api.Tests.Services.Ai;

public sealed class ResearchAnswerServiceObservabilityTests
{
    [Fact]
    public async Task AskAsync_DebugEnabled_CorrelatesTraceAndRecordsSafeAttributes()
    {
        var activities = new ConcurrentBag<Activity>();
        using var listener = CreateListener(activities);
        var question = "private-question-that-must-not-be-a-tag";
        var service = CreateService(new SuccessfulChatService());

        var response = await service.AskAsync(new ResearchAskRequest(
            Ticker: "2330",
            Question: question,
            TopK: 2,
            Debug: true));

        Assert.NotNull(response.Trace);
        var ask = Assert.Single(activities, activity =>
            activity.OperationName == "research.ask" && activity.TraceId.ToString() == response.Trace.TraceId);
        Assert.Equal(ask.TraceId.ToString(), response.Trace.TraceId);
        Assert.Equal(
            response.Trace.Retrieval.CandidateCount,
            response.Trace.Retrieval.SelectedCount + response.Trace.Retrieval.DiscardedCount);

        var names = activities.Select(activity => activity.OperationName).ToHashSet();
        Assert.Contains("intent.detect", names);
        Assert.Contains("retrieval.plan", names);
        Assert.Contains("retrieval.search", names);
        Assert.Contains("rerank", names);
        Assert.Contains("deduplicate", names);
        Assert.Contains("llm.complete", names);

        Assert.DoesNotContain(activities.SelectMany(activity => activity.TagObjects), tag =>
            tag.Value?.ToString()?.Contains(question, StringComparison.Ordinal) == true);
        Assert.Equal(ActivityStatusCode.Ok, ask.Status);
    }

    [Fact]
    public async Task AskAsync_DebugDisabled_DoesNotReturnTrace()
    {
        using var listener = CreateListener(new ConcurrentBag<Activity>());
        var service = CreateService(new SuccessfulChatService());

        var response = await service.AskAsync(new ResearchAskRequest(
            Ticker: "2330",
            Question: "公司表現",
            TopK: 2,
            Debug: false));

        Assert.Null(response.Trace);
    }

    [Fact]
    public async Task AskAsync_LlmFailure_MarksLlmAndAskSpansAsError()
    {
        var activities = new ConcurrentBag<Activity>();
        using var listener = CreateListener(activities);
        var service = CreateService(new FailingChatService());

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.AskAsync(new ResearchAskRequest(
            Ticker: "2330",
            Question: "公司表現",
            TopK: 2,
            Debug: true)));

        var askActivities = activities.Where(activity => activity.OperationName == "research.ask").ToList();
        var llmCompleteActivities = activities.Where(activity => activity.OperationName == "llm.complete").ToList();

        Assert.Contains(askActivities, activity => activity.Status == ActivityStatusCode.Error);

        var failedAsk = askActivities.First(activity => activity.Status == ActivityStatusCode.Error);
        var failedLlm = Assert.Single(llmCompleteActivities, activity => activity.TraceId == failedAsk.TraceId);
        Assert.Equal(ActivityStatusCode.Error, failedLlm.Status);
    }

    private static ResearchAnswerService CreateService(IChatCompletionService chatService)
    {
        var options = Options.Create(new RetrievalOptions());
        var documentRetriever = new DocumentRetriever(new FakeDocumentSearchService(), options);
        var reranker = new ResultReranker(options);
        var contextSelector = new ContextSelector(options, NullLogger<ContextSelector>.Instance);
        var formatter = new ContextFormatter();
        var citationValidator = new CitationValidator();
        var answerGenerator = new AnswerGenerator(
            chatService,
            citationValidator,
            options,
            NullLogger<AnswerGenerator>.Instance);

        return new ResearchAnswerService(
            new IntentDetector(),
            new RetrievalPlanner(options),
            documentRetriever,
            new FakeWebRetriever(),
            reranker,
            contextSelector,
            formatter,
            answerGenerator,
            options,
            NullLogger<ResearchAnswerService>.Instance);
    }

    private static ActivityListener CreateListener(ConcurrentBag<Activity> activities)
    {
        var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == EquityLensTelemetry.ActivitySourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activities.Add
        };
        ActivitySource.AddActivityListener(listener);
        return listener;
    }

    private sealed class FakeDocumentSearchService : IDocumentSearchService
    {
        public Task<DocumentSearchResponse> SearchAsync(
            DocumentSearchRequest request,
            CancellationToken cancellationToken = default)
        {
            var results = new[]
            {
                CreateResult(1, 0.95, "First unique evidence about company operations and performance."),
                CreateResult(2, 0.90, "Second distinct evidence about company strategy and execution."),
                CreateResult(3, 0.85, "Third independent evidence about market position and products.")
            };
            return Task.FromResult(new DocumentSearchResponse(request.Query, "test-embedding", results));
        }

        private static DocumentSearchResult CreateResult(int index, double relevance, string content)
        {
            return new DocumentSearchResult(
                DocumentChunkId: Guid.NewGuid(),
                DocumentId: Guid.NewGuid(),
                DocumentTitle: $"Document {index}",
                DocumentType: "AnnualReport",
                SourceUrl: null,
                ChunkIndex: index,
                PageNumber: index,
                SectionTitle: null,
                Content: content,
                Distance: 1 - relevance,
                RelevanceScore: relevance,
                SecurityId: null,
                Ticker: "2330",
                Exchange: "TWSE",
                SecurityName: "Test Security");
        }
    }

    private sealed class FakeWebRetriever : IWebRetriever
    {
        public Task<IReadOnlyList<RetrievedDocumentChunk>> RetrieveWebAsync(
            string query, int count, string? freshness, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<RetrievedDocumentChunk>>([]);
        }
    }

    private sealed class FakeJinaReranker : IJinaReranker
    {
        public Task<IReadOnlyList<RetrievedDocumentChunk>> RerankAsync(
            string query, IReadOnlyList<RetrievedDocumentChunk> chunks, int topN, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<RetrievedDocumentChunk>>(chunks.Take(topN).ToList());
        }
    }

    private sealed class SuccessfulChatService : IChatCompletionService
    {
        public string Provider => "test";
        public string Model => "test-model";

        public Task<ChatCompletionResult> CompleteAsync(
            ChatCompletionRequest request,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new ChatCompletionResult("answer [1]", Model, 100, 20));
        }
    }

    private sealed class FailingChatService : IChatCompletionService
    {
        public string Provider => "test";
        public string Model => "test-model";

        public Task<ChatCompletionResult> CompleteAsync(
            ChatCompletionRequest request,
            CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("simulated failure");
        }
    }
}
