using EquityLens.Api.Contracts.Research;
using EquityLens.Api.Services.Ai;
using EquityLens.Api.Services.Ai.Retrieval;
using EquityLens.Api.Services.CurrentUser;
using EquityLens.Api.Services.Documents;
using EquityLens.Api.Services.Research;
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

    [Fact]
    public async Task AskAsync_InvalidCitation_RetryPromptUsesMaxValidCitationIndex()
    {
        var chatService = new CapturingRetryChatService();
        var service = CreateService(
            new ThreeResultDocumentSearchService(),
            chatService);

        await service.AskAsync(new ResearchAskRequest(
            Ticker: "2330",
            Question: "主要風險是什麼？"));

        Assert.Equal(2, chatService.UserPrompts.Count);
        Assert.Contains("[1] 到 [3]", chatService.UserPrompts[1]);
        Assert.DoesNotContain("[1] 到 [5]", chatService.UserPrompts[1]);
    }

    [Fact]
    public async Task AskAsync_RiskAutoWithoutPrimaryConferenceSelection_PassesRetrievalNoteToPromptAndTrace()
    {
        var chatService = new PromptCapturingChatService();
        var service = CreateService(
            new ConferenceSearchWithoutRiskEvidenceService(),
            chatService);

        var response = await service.AskAsync(new ResearchAskRequest(
            Ticker: "2330",
            Question: "主要風險是什麼？",
            TopK: 2,
            Debug: true));

        Assert.Contains("系統已搜尋 Primary 來源的法說會簡報", response.Trace!.RetrievalNote);
        Assert.Contains("系統已搜尋 Primary 來源的法說會簡報", chatService.LastUserPrompt);
        Assert.Single(response.Citations);
        Assert.Equal("AnnualReport", response.Citations[0].DocumentType);
    }

    [Fact]
    public async Task AskAsync_AllCandidatesBelowMinimumCandidateScore_ReturnsInsufficientEvidenceWithoutLlm()
    {
        var chatService = new CallTrackingChatService();
        var service = CreateService(
            new LowScoreDocumentSearchService(),
            chatService,
            configureOptions: options => options.MinimumCandidateScore = 0.9);

        var response = await service.AskAsync(new ResearchAskRequest(
            Ticker: "2330",
            Question: "公司表現",
            Debug: true));

        Assert.Equal("InsufficientEvidence", response.Status);
        Assert.False(chatService.WasCalled);
        Assert.Contains(response.Trace!.Results, result => result.Decision == "DiscardedByMinimumCandidateScore");
    }

    [Fact]
    public async Task AskAsync_LocalAndWebMerge_CapsContextAtTopKAndKeepsLocalFirst()
    {
        var service = CreateService(
            new ThreeResultDocumentSearchService(),
            new SuccessfulChatService(),
            webRetriever: new ManyWebResultsRetriever());

        var response = await service.AskAsync(new ResearchAskRequest(
            Ticker: "2330",
            Question: "主要風險是什麼？",
            DocumentType: "AnnualReport",
            SourcePolicy: SourcePolicy.LocalAndWeb,
            TopK: 2));

        Assert.Equal(2, response.Citations.Count);
        Assert.All(response.Citations, citation => Assert.Equal(CitationSourceType.LocalDocument, citation.SourceType));
    }

    [Fact]
    public async Task AskAsync_LlmReturnsInsufficientEvidence_SetsStatusAndKeepsCitations()
    {
        var chatService = new InsufficientEvidenceChatService();
        var service = CreateService(
            new ThreeResultDocumentSearchService(),
            chatService);

        var response = await service.AskAsync(new ResearchAskRequest(
            Ticker: "2330",
            Question: "台積電下一季股價會漲到多少？",
            TopK: 2,
            Debug: true));

        Assert.Equal("InsufficientEvidence", response.Status);
        Assert.NotEmpty(response.Citations);
        Assert.NotNull(response.Trace);
        Assert.Equal(response.Citations.Count, response.Trace.Retrieval.FinalCitationCount);
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
        IChatCompletionService chatService,
        Action<RetrievalOptions>? configureOptions = null,
        IWebRetriever? webRetriever = null)
    {
        var retrievalOptions = new RetrievalOptions();
        configureOptions?.Invoke(retrievalOptions);
        var options = Options.Create(retrievalOptions);
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
            webRetriever ?? new FakeWebRetriever(),
            reranker,
            contextSelector,
            formatter,
            new ChunkContentCleaner(),
            answerGenerator,
            options,
            new FakeResearchRunTraceService(),
            new FakeCurrentUserContext(),
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

    private sealed class LowScoreDocumentSearchService : IDocumentSearchService
    {
        public Task<DocumentSearchResponse> SearchAsync(
            DocumentSearchRequest request,
            CancellationToken cancellationToken = default)
        {
            var results = new[]
            {
                CreateResult(1, 0.20, "Low score evidence about company operations."),
                CreateResult(2, 0.10, "Another low score evidence about company strategy.")
            };
            return Task.FromResult(new DocumentSearchResponse(request.Query, "test-embedding", results));
        }

        private static DocumentSearchResult CreateResult(int index, double relevance, string content)
        {
            return new DocumentSearchResult(
                DocumentChunkId: Guid.NewGuid(),
                DocumentId: Guid.NewGuid(),
                DocumentTitle: $"Low Score Document {index}",
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

    private sealed class ConferenceSearchWithoutRiskEvidenceService : IDocumentSearchService
    {
        public Task<DocumentSearchResponse> SearchAsync(
            DocumentSearchRequest request,
            CancellationToken cancellationToken = default)
        {
            var result = request.DocumentType == "EarningsPresentation"
                ? CreateResult("Conference Agenda", "EarningsPresentation", 0.95, "Agenda and presentation overview with schedule and speaker names only.")
                : CreateResult("Annual Risk Factors", "AnnualReport", 0.90, "Risk factors include market risk and uncertainty in demand.");

            return Task.FromResult(new DocumentSearchResponse(request.Query, "test-embedding", [result]));
        }

        private static DocumentSearchResult CreateResult(string title, string documentType, double relevance, string content)
        {
            return new DocumentSearchResult(
                DocumentChunkId: Guid.NewGuid(),
                DocumentId: Guid.NewGuid(),
                DocumentTitle: title,
                DocumentType: documentType,
                SourceUrl: null,
                ChunkIndex: 1,
                PageNumber: 2,
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

    private sealed class CapturingRetryChatService : IChatCompletionService
    {
        public List<string> UserPrompts { get; } = [];

        public string Provider => "test";
        public string Model => "test-model";

        public Task<ChatCompletionResult> CompleteAsync(
            ChatCompletionRequest request,
            CancellationToken cancellationToken = default)
        {
            UserPrompts.Add(request.UserPrompt);
            var answer = UserPrompts.Count == 1
                ? "answer with [5]"
                : "valid retry answer [1]";
            return Task.FromResult(new ChatCompletionResult(answer, Model, 10, 5));
        }
    }

    private sealed class PromptCapturingChatService : IChatCompletionService
    {
        public string LastUserPrompt { get; private set; } = string.Empty;

        public string Provider => "test";
        public string Model => "test-model";

        public Task<ChatCompletionResult> CompleteAsync(
            ChatCompletionRequest request,
            CancellationToken cancellationToken = default)
        {
            LastUserPrompt = request.UserPrompt;
            return Task.FromResult(new ChatCompletionResult("answer [1]", Model, 10, 5));
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

    private sealed class ManyWebResultsRetriever : IWebRetriever
    {
        public Task<IReadOnlyList<RetrievedDocumentChunk>> RetrieveWebAsync(
            string query, int count, string? freshness, CancellationToken cancellationToken = default)
        {
            var results = Enumerable.Range(1, 5)
                .Select(index => new RetrievedDocumentChunk(
                    new DocumentSearchResult(
                        DocumentChunkId: Guid.NewGuid(),
                        DocumentId: Guid.NewGuid(),
                        DocumentTitle: $"Web Result {index}",
                        DocumentType: "WebSearch",
                        SourceUrl: $"https://example.com/{index}",
                        ChunkIndex: index,
                        PageNumber: null,
                        SectionTitle: null,
                        Content: $"Web risk evidence {index}",
                        Distance: 0,
                        RelevanceScore: 0,
                        SecurityId: null,
                        Ticker: null,
                        Exchange: null,
                        SecurityName: null),
                    SourceRole: "Web",
                    SearchId: "web-search",
                    Query: query,
                    SourceType: CitationSourceType.Web,
                    Url: $"https://example.com/{index}"))
                .ToList();

            return Task.FromResult<IReadOnlyList<RetrievedDocumentChunk>>(results);
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
        public Task<DocumentRerankResult> RerankAsync(
            string query, IReadOnlyList<RetrievedDocumentChunk> chunks, int topN, CancellationToken cancellationToken = default)
        {
            var results = chunks.Take(topN).ToList();
            return Task.FromResult(new DocumentRerankResult(results, new("Test", "Succeeded", "test", false, null, 1, chunks.Count, results.Count, 0, 200)));
        }
    }

    private sealed class InsufficientEvidenceChatService : IChatCompletionService
    {
        public string Provider => "test";
        public string Model => "test-model";

        public Task<ChatCompletionResult> CompleteAsync(
            ChatCompletionRequest request,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new ChatCompletionResult(
                "目前提供的資料不足以回答此問題。",
                Model, 10, 5));
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
        public Guid UserId => Guid.Empty;
        public string Email => "test@example.test";
        public string DisplayName => "Test User";
        public bool IsAuthenticated => true;
    }
}
