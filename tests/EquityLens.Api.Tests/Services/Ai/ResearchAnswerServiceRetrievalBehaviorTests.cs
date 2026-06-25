using EquityLens.Api.Contracts.Research;
using EquityLens.Api.Services.Ai;
using EquityLens.Api.Services.Ai.Retrieval;
using EquityLens.Api.Services.Documents;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace EquityLens.Api.Tests.Services.Ai;

public sealed class ResearchAnswerServiceRetrievalBehaviorTests
{
    [Fact]
    public async Task AskAsync_NoSearchResults_DoesNotCallLlmAndReturnsInsufficientEvidence()
    {
        var chatService = new CallTrackingChatService();
        var service = CreateService(
            new EmptyDocumentSearchService(),
            chatService);

        var response = await service.AskAsync(new ResearchAskRequest(
            Ticker: "2330",
            Question: "主要風險是什麼？"));

        Assert.Equal("目前提供的資料不足以回答此問題。", response.Answer);
        Assert.Equal("InsufficientEvidence", response.Status);
        Assert.Empty(response.Citations);
        Assert.False(chatService.WasCalled);
    }

    [Fact]
    public async Task AskAsync_InvalidCitation_RetriesAndThenSanitizes()
    {
        var chatService = new AlwaysInvalidCitationChatService();
        var service = CreateService(
            new ThreeResultDocumentSearchService(),
            chatService);

        var response = await service.AskAsync(new ResearchAskRequest(
            Ticker: "2330",
            Question: "主要風險是什麼？"));

        Assert.Equal(1 + RetrievalOptionsDefaults.MaxCitationRetries, chatService.CallCount);
        Assert.DoesNotContain("[5]", response.Answer);
        Assert.DoesNotContain("[99]", response.Answer);
    }

    [Fact]
    public async Task AskAsync_InvalidCitation_RetrySucceeds_UsesRetryAnswer()
    {
        var chatService = new FixedAfterRetryChatService();
        var service = CreateService(
            new ThreeResultDocumentSearchService(),
            chatService);

        var response = await service.AskAsync(new ResearchAskRequest(
            Ticker: "2330",
            Question: "主要風險是什麼？"));

        Assert.Equal(2, chatService.CallCount);
        Assert.Equal("valid retry answer [1]", response.Answer);
    }

    [Theory]
    [InlineData("請說明台積電法說會的業績展望", "2026Q2_M001_zh.pdf", "2026Q2_E001_en.pdf")]
    [InlineData("Summarize TSMC conference outlook", "2026Q2_E001_en.pdf", "2026Q2_M001_zh.pdf")]
    public async Task AskAsync_DeduplicatesPresentationLanguages_PrefersQuestionLanguage(
        string question,
        string expectedTitle,
        string duplicateTitle)
    {
        var service = CreateService(
            new PresentationLanguageDuplicateSearchService(),
            new SuccessfulChatService());

        var response = await service.AskAsync(new ResearchAskRequest(
            Ticker: "2330",
            Question: question,
            RetrievalMode: RetrievalMode.ConferenceOnly,
            TopK: 2,
            Debug: true));

        Assert.Contains(response.Citations, citation => citation.Title == expectedTitle && citation.PageNumber == 10);
        Assert.DoesNotContain(response.Citations, citation => citation.Title == duplicateTitle && citation.PageNumber == 10);
        Assert.Contains(response.Trace!.Results, decision =>
            decision.Decision == "DiscardedByPresentationLanguageDedup"
            && decision.DocumentTitle == duplicateTitle
            && decision.DuplicateOfChunkId is not null);
    }

    private static ResearchAnswerService CreateService(
        IDocumentSearchService documentSearchService,
        IChatCompletionService chatService)
    {
        var options = Options.Create(new RetrievalOptions());
        var documentRetriever = new DocumentRetriever(documentSearchService, options);
        var reranker = new ResultReranker(options, new ChunkContentCleaner());
        var contextSelector = new ContextSelector(options, NullLogger<ContextSelector>.Instance);
        var formatter = new ContextFormatter(new ChunkContentCleaner());
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
            new ChunkContentCleaner(),
            answerGenerator,
            options,
            NullLogger<ResearchAnswerService>.Instance);
    }

    private static class RetrievalOptionsDefaults
    {
        public const int MaxCitationRetries = 1;
    }

    private sealed class EmptyDocumentSearchService : IDocumentSearchService
    {
        public Task<DocumentSearchResponse> SearchAsync(
            DocumentSearchRequest request,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new DocumentSearchResponse(request.Query, "test-embedding", []));
        }
    }

    private sealed class ThreeResultDocumentSearchService : IDocumentSearchService
    {
        public Task<DocumentSearchResponse> SearchAsync(
            DocumentSearchRequest request,
            CancellationToken cancellationToken = default)
        {
            var results = new[]
            {
                CreateResult(1, 0.95, "First evidence about company risk factors."),
                CreateResult(2, 0.90, "Second evidence about market uncertainty."),
                CreateResult(3, 0.85, "Third evidence about operational challenges.")
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

    private sealed class PresentationLanguageDuplicateSearchService : IDocumentSearchService
    {
        public Task<DocumentSearchResponse> SearchAsync(
            DocumentSearchRequest request,
            CancellationToken cancellationToken = default)
        {
            var results = new[]
            {
                CreateResult(
                    Guid.NewGuid(),
                    "2026Q2_M001_zh.pdf",
                    10,
                    0.92,
                    "Language: zh-TW\n2026年第二季業績展望，HPC 和 AI 需求持續強勁，資本支出與現金流展望穩健。"),
                CreateResult(
                    Guid.NewGuid(),
                    "2026Q2_E001_en.pdf",
                    10,
                    0.91,
                    "Language: en\nFuture outlook and guidance show strong HPC and AI demand, with resilient capex and cash flow outlook."),
                CreateResult(
                    Guid.NewGuid(),
                    "2026Q2_M001_zh.pdf",
                    11,
                    0.80,
                    "Language: zh-TW\n其他法說會補充內容，說明毛利率、需求與業績展望。")
            };
            return Task.FromResult(new DocumentSearchResponse(request.Query, "test-embedding", results));
        }

        private static DocumentSearchResult CreateResult(
            Guid documentId,
            string title,
            int pageNumber,
            double relevance,
            string content)
        {
            return new DocumentSearchResult(
                DocumentChunkId: Guid.NewGuid(),
                DocumentId: documentId,
                DocumentTitle: title,
                DocumentType: "EarningsPresentation",
                SourceUrl: null,
                ChunkIndex: pageNumber,
                PageNumber: pageNumber,
                SectionTitle: null,
                Content: content,
                Distance: 1 - relevance,
                RelevanceScore: relevance,
                SecurityId: null,
                Ticker: "2330",
                Exchange: "TWSE",
                SecurityName: "TSMC");
        }
    }

    private sealed class CallTrackingChatService : IChatCompletionService
    {
        public bool WasCalled { get; private set; }

        public string Provider => "test";
        public string Model => "test-model";

        public Task<ChatCompletionResult> CompleteAsync(
            ChatCompletionRequest request,
            CancellationToken cancellationToken = default)
        {
            WasCalled = true;
            return Task.FromResult(new ChatCompletionResult("unexpected answer", Model, 0, 0));
        }
    }

    private sealed class AlwaysInvalidCitationChatService : IChatCompletionService
    {
        public int CallCount { get; private set; }

        public string Provider => "test";
        public string Model => "test-model";

        public Task<ChatCompletionResult> CompleteAsync(
            ChatCompletionRequest request,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(new ChatCompletionResult("answer with [5] and [99]", Model, 10, 5));
        }
    }

    private sealed class FixedAfterRetryChatService : IChatCompletionService
    {
        public int CallCount { get; private set; }

        public string Provider => "test";
        public string Model => "test-model";

        public Task<ChatCompletionResult> CompleteAsync(
            ChatCompletionRequest request,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            var answer = CallCount == 1
                ? "answer with [5]"
                : "valid retry answer [1]";
            return Task.FromResult(new ChatCompletionResult(answer, Model, 10, 5));
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
            return Task.FromResult(new ChatCompletionResult("answer [1]", Model, 10, 5));
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

    private sealed class FakeReranker : IDocumentReranker
    {
        public Task<IReadOnlyList<RetrievedDocumentChunk>> RerankAsync(
            string query, IReadOnlyList<RetrievedDocumentChunk> chunks, int topN, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<RetrievedDocumentChunk>>(chunks.Take(topN).ToList());
        }
    }
}
