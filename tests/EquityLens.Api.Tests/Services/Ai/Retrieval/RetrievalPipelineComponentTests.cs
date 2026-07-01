using System.Text;
using System.Text.Json;
using EquityLens.Api.Contracts.Research;
using EquityLens.Api.Services.Ai;
using EquityLens.Api.Services.Ai.Retrieval;
using Microsoft.Extensions.Options;

namespace EquityLens.Api.Tests.Services.Ai.Retrieval;

public sealed class RetrievalPipelineComponentTests
{
    public class IntentDetectorTests
    {
        private readonly IntentDetector _detector = new();

        [Theory]
        [InlineData("這家公司有什麼風險？", ResearchQuestionIntent.Risk)]
        [InlineData("台積電面臨哪些挑戰與不確定性？", ResearchQuestionIntent.Risk)]
        [InlineData("營收和毛利率表現如何？", ResearchQuestionIntent.Financial)]
        [InlineData("每股盈餘與現金流狀況？", ResearchQuestionIntent.Financial)]
        [InlineData("管理層對未來展望如何？", ResearchQuestionIntent.Outlook)]
        [InlineData("guidance 與 business outlook 為何？", ResearchQuestionIntent.Outlook)]
        [InlineData("這家公司的基本資料是什麼？", ResearchQuestionIntent.General)]
        public void Detect_VariousQuestions_ReturnsExpectedIntent(string question, ResearchQuestionIntent expected)
        {
            var result = _detector.Detect(question);
            Assert.Equal(expected, result.Selected);
        }
    }

    public class RetrievalPlannerTests
    {
        private readonly RetrievalPlanner _planner;

        public RetrievalPlannerTests()
        {
            _planner = new RetrievalPlanner(Options.Create(new RetrievalOptions()));
        }

        [Theory]
        [InlineData(RetrievalMode.ConferenceOnly, "EarningsPresentation")]
        [InlineData(RetrievalMode.AnnualReportOnly, "AnnualReport")]
        [InlineData(RetrievalMode.AllDocuments, null)]
        public void BuildPlan_ExplicitModes_UsesExpectedDocumentType(RetrievalMode mode, string? expectedDocumentType)
        {
            var plan = _planner.BuildPlan("公司風險為何？", mode, documentType: null, topK: 10);

            Assert.Equal(mode.ToString(), plan.Mode);
            Assert.Single(plan.Searches);
            Assert.Equal(expectedDocumentType, plan.Searches[0].DocumentType);
        }

        [Fact]
        public void BuildPlan_DocumentTypeOverride_IgnoresRetrievalMode()
        {
            var plan = _planner.BuildPlan(
                "公司風險為何？",
                RetrievalMode.ConferenceOnly,
                documentType: "AnnualReport",
                topK: 10);

            Assert.Equal("DocumentTypeOverride", plan.Mode);
            Assert.Single(plan.Searches);
            Assert.Equal("AnnualReport", plan.Searches[0].DocumentType);
        }

        [Fact]
        public void BuildPlan_AutoRiskQuestion_SplitsTopKBetweenConferenceAndAnnualReport()
        {
            var plan = _planner.BuildPlan("主要風險是什麼？", RetrievalMode.Auto, documentType: null, topK: 10);

            Assert.Equal("Auto", plan.Mode);
            Assert.Equal(2, plan.Searches.Count);
            Assert.Equal("EarningsPresentation", plan.Searches[0].DocumentType);
            Assert.Equal("AnnualReport", plan.Searches[1].DocumentType);
            Assert.Equal(7, plan.Searches[0].TopK);
            Assert.Equal(3, plan.Searches[1].TopK);
        }
    }

    public class ChunkContentCleanerTests
    {
        private readonly ChunkContentCleaner _cleaner = new();

        [Fact]
        public void Clean_RemovesStructuredChunkMetadataAndKeepsBody()
        {
            var cleaned = _cleaner.Clean("Company: 台積電\nTicker: 2330\nExchange: TWSE\nDocumentType: EarningsPresentation\nSource: InvestorConference\nConferenceDate: unknown\nLanguage: zh-TW\nPage: 9\n2026年第二季業績展望 合併營收 毛利率");

            Assert.DoesNotContain("Company:", cleaned);
            Assert.DoesNotContain("Ticker:", cleaned);
            Assert.DoesNotContain("DocumentType:", cleaned);
            Assert.Contains("2026年第二季業績展望", cleaned);
            Assert.Contains("毛利率", cleaned);
        }

        [Fact]
        public void Clean_RemovesInvestorPresentationBoilerplateAndKeepsEvidence()
        {
            var cleaned = _cleaner.Clean("UnleashInnovation©92026TSMC, LtdTSMC PropertyFuture OutlookBased on our current business outlook, management expects revenue growth. https://www.tsmc.com invest@tsmc.com");

            Assert.DoesNotContain("UnleashInnovation", cleaned);
            Assert.DoesNotContain("TSMC Property", cleaned);
            Assert.DoesNotContain("https://www.tsmc.com", cleaned);
            Assert.DoesNotContain("invest@tsmc.com", cleaned);
            Assert.Contains("Future OutlookBased", cleaned);
            Assert.Contains("management expects revenue growth", cleaned);
        }
    }

    public class ResultRerankerTests
    {
        private readonly ResultReranker _reranker;

        public ResultRerankerTests()
        {
            _reranker = new ResultReranker(Options.Create(new RetrievalOptions()), new ChunkContentCleaner());
        }

        [Fact]
        public async Task Rank_RiskIntentWithoutRiskEvidence_DiscardsAllCandidates()
        {
            var chunks = new[]
            {
                CreateChunk("This is a generic statement about company operations.", 0.95),
                CreateChunk("Another neutral sentence about business activities and market position.", 0.90)
            };

            var result = await _reranker.Rank(chunks, ResearchQuestionIntent.Risk, topK: 5);

            Assert.Empty(result.SelectedResults);
            Assert.All(result.Decisions, d => Assert.Equal("DiscardedByRiskEvidenceFilter", d.Decision));
        }

        [Fact]
        public async Task Rank_TopKLimit_KeepsOnlyTopK()
        {
            var chunks = Enumerable.Range(1, 10)
                .Select(i => CreateChunk($"Risk factor number {i}.", 0.5 + i * 0.05))
                .ToList();

            var result = await _reranker.Rank(chunks, ResearchQuestionIntent.Risk, topK: 3);

            Assert.Equal(3, result.SelectedResults.Count);
            Assert.Contains(result.Decisions, d => d.Decision == "DiscardedByTopK");
        }

        [Fact]
        public async Task Rank_DuplicatePage_KeepsOnlyOnePerPage()
        {
            var documentId = Guid.NewGuid();
            var chunks = new[]
            {
                CreateChunk("Risk factor discussed on page 2.", 0.95, documentId, pageNumber: 2),
                CreateChunk("Another risk discussion on page 2.", 0.94, documentId, pageNumber: 2)
            };

            var result = await _reranker.Rank(chunks, ResearchQuestionIntent.Risk, topK: 5);

            Assert.Single(result.SelectedResults);
            Assert.Contains(result.Decisions, d => d.Decision == "DiscardedByPageDedup");
        }

        [Fact]
        public async Task Rank_SafeHarborLimit_KeepsOnlyOneSafeHarbor()
        {
            var chunks = new[]
            {
                CreateChunk("Safe Harbor Notice: forward-looking statements.", 0.95),
                CreateChunk("Another Safe Harbor Notice about forward-looking statements.", 0.94),
                CreateChunk("General company information here.", 0.93)
            };

            var result = await _reranker.Rank(chunks, ResearchQuestionIntent.General, topK: 5);

            Assert.Equal(2, result.SelectedResults.Count);
            Assert.Contains(result.Decisions, d => d.Decision == "DiscardedBySafeHarborLimit");
        }


        [Fact]
        public async Task Rank_EarningsPresentationMetadataPrefix_DoesNotDeduplicateDistinctPages()
        {
            var documentId = Guid.NewGuid();
            var chunks = new[]
            {
                CreateChunk("Company: 台積電\nTicker: 2330\nExchange: TWSE\nDocumentType: EarningsPresentation\nSource: InvestorConference\nConferenceDate: unknown\nLanguage: zh-TW\nPage: 8\n現金流量表 自由現金流量 營運活動之現金流入 資本支出", 0.95, documentId, pageNumber: 8, documentType: "EarningsPresentation"),
                CreateChunk("Company: 台積電\nTicker: 2330\nExchange: TWSE\nDocumentType: EarningsPresentation\nSource: InvestorConference\nConferenceDate: unknown\nLanguage: zh-TW\nPage: 9\n2026年第二季業績展望 合併營收 毛利率 營業利益率", 0.94, documentId, pageNumber: 9, documentType: "EarningsPresentation")
            };

            var result = await _reranker.Rank(chunks, ResearchQuestionIntent.Outlook, topK: 5);

            Assert.Equal(2, result.SelectedResults.Count);
            Assert.DoesNotContain(result.Decisions, d => d.Decision == "DiscardedByContentDedup");
        }

        [Fact]
        public async Task Rank_RiskIntent_KeepsPrimaryConferenceOutlookEvidence()
        {
            var chunks = new[]
            {
                CreateChunk("Future outlook: management expects 2026 revenue growth and cash flow for capital expenditure.", 0.95, documentType: "EarningsPresentation")
            };

            var result = await _reranker.Rank(chunks, ResearchQuestionIntent.Risk, topK: 5);

            Assert.Single(result.SelectedResults);
            Assert.Equal("Selected", Assert.Single(result.Decisions).Decision);
        }

        [Fact]
        public async Task Rank_FinancialIntent_CashFlowChunkGetsBonus()
        {
            var chunks = new[]
            {
                CreateChunk("營運活動之現金流入 698.97 資本支出 350.76 自由現金流量 348.21", 0.80, documentType: "EarningsPresentation"),
                CreateChunk("General company information about products", 0.80, documentType: "EarningsPresentation")
            };

            var result = await _reranker.Rank(chunks, ResearchQuestionIntent.Financial, topK: 2);

            Assert.Equal(2, result.SelectedResults.Count);
            var cashFlow = result.SelectedResults.First(c => c.Result.Content.Contains("現金流入"));
            var general = result.SelectedResults.First(c => c.Result.Content.Contains("General"));
            var cashFlowScore = result.Decisions.First(d => d.Chunk.Result.DocumentChunkId == cashFlow.Result.DocumentChunkId);
            var generalScore = result.Decisions.First(d => d.Chunk.Result.DocumentChunkId == general.Result.DocumentChunkId);
            Assert.True(cashFlowScore.AdjustedScore > generalScore.AdjustedScore,
                "Cash-flow chunk should outrank general chunk for Financial intent");
        }

        [Fact]
        public async Task Rank_FinancialIntent_WeakFinancialPageGetsPenalty()
        {
            var chunks = new[]
            {
                CreateChunk("資金貸與他人 本公司資金貸與他人民國114年1月1日至12月31日", 0.95, documentType: "AnnualReport"),
                CreateChunk("現金流量表 營運活動之現金流入 698.97 資本支出 350.76", 0.90, documentType: "AnnualReport")
            };

            var result = await _reranker.Rank(chunks, ResearchQuestionIntent.Financial, topK: 2);

            Assert.Equal(2, result.SelectedResults.Count);
            var cashFlowChunk = result.SelectedResults.First(c => c.Result.Content.Contains("現金流量表"));
            var loanChunk = result.SelectedResults.First(c => c.Result.Content.Contains("資金貸與"));
            var cashFlowDecision = result.Decisions.First(d => d.Chunk.Result.DocumentChunkId == cashFlowChunk.Result.DocumentChunkId);
            var loanDecision = result.Decisions.First(d => d.Chunk.Result.DocumentChunkId == loanChunk.Result.DocumentChunkId);
            Assert.True(cashFlowDecision.AdjustedScore > loanDecision.AdjustedScore,
                "Cash-flow chunk should rank higher than weak financial page for Financial intent");
        }

        [Fact]
        public async Task Rank_FinalScoreBelowMinimum_DiscardsCandidate()
        {
            var reranker = new ResultReranker(
                Options.Create(new RetrievalOptions { MinimumFinalScore = 0.9 }),
                new ChunkContentCleaner());
            var chunks = new[]
            {
                CreateChunk("Risk factor discussed with uncertainty and market risk.", 0.50)
            };

            var result = await reranker.Rank(chunks, ResearchQuestionIntent.Risk, topK: 5);

            Assert.Empty(result.SelectedResults);
            Assert.Contains(result.Decisions, d => d.Decision == "DiscardedByMinimumFinalScore");
        }


        private static RetrievedDocumentChunk CreateChunk(
            string content,
            double relevance,
            Guid? documentId = null,
            int? pageNumber = null,
            string documentType = "AnnualReport",
            string sourceRole = "Primary")
        {
            return new RetrievedDocumentChunk(
                new DocumentSearchResult(
                    DocumentChunkId: Guid.NewGuid(),
                    DocumentId: documentId ?? Guid.NewGuid(),
                    DocumentTitle: "Test Document",
                    DocumentType: documentType,
                    SourceUrl: null,
                    ChunkIndex: 1,
                    PageNumber: pageNumber,
                    SectionTitle: null,
                    Content: content,
                    Distance: 1 - relevance,
                    RelevanceScore: relevance,
                    SecurityId: null,
                    Ticker: "2330",
                    Exchange: "TWSE",
                    SecurityName: "Test"),
                SourceRole: sourceRole,
                SearchId: "search-1",
                Query: "test");
        }
    }

    public class CitationValidatorTests
    {
        private readonly CitationValidator _validator = new();

        [Theory]
        [InlineData("answer [1] and [2]", 3, true)]
        [InlineData("answer [1] and [5]", 3, false)]
        [InlineData("answer with [0]", 3, false)]
        [InlineData("no citations", 3, true)]
        public void Validate_VariousInputs_ReturnsExpectedResult(string answer, int maxIndex, bool expectedValid)
        {
            var result = _validator.Validate(answer, maxIndex);
            Assert.Equal(expectedValid, result.IsValid);
        }

        [Fact]
        public void Validate_InvalidCitation_RemovesInvalidMarkers()
        {
            var result = _validator.Validate("answer [1] and [5] here", maxValidIndex: 3);

            Assert.False(result.IsValid);
            Assert.Contains(5, result.InvalidIndices);
            Assert.DoesNotContain("[5]", result.SanitizedAnswer);
            Assert.Contains("[1]", result.SanitizedAnswer);
        }
    }

    public class RetrievalModeJsonConverterTests
    {
        private readonly JsonSerializerOptions _options = new(JsonSerializerDefaults.Web);

        [Theory]
        [InlineData("\"Auto\"", RetrievalMode.Auto)]
        [InlineData("\"ConferenceOnly\"", RetrievalMode.ConferenceOnly)]
        [InlineData("\"AnnualReportOnly\"", RetrievalMode.AnnualReportOnly)]
        [InlineData("\"AllDocuments\"", RetrievalMode.AllDocuments)]
        [InlineData("\"conferenceOnly\"", RetrievalMode.ConferenceOnly)]
        public void Read_ValidString_ReturnsExpectedMode(string json, RetrievalMode expected)
        {
            var mode = JsonSerializer.Deserialize<RetrievalMode>(json, _options);
            Assert.Equal(expected, mode);
        }

        [Theory]
        [InlineData("999")]
        [InlineData("\"InvalidMode\"")]
        [InlineData("null")]
        public void Read_InvalidOrNumber_ThrowsJsonException(string json)
        {
            Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<RetrievalMode>(json, _options));
        }
    }

    public class SourcePolicyJsonConverterTests
    {
        private readonly JsonSerializerOptions _options = new(JsonSerializerDefaults.Web);

        [Theory]
        [InlineData("\"LocalOnly\"", SourcePolicy.LocalOnly)]
        [InlineData("\"LocalThenWeb\"", SourcePolicy.LocalThenWeb)]
        [InlineData("\"LocalAndWeb\"", SourcePolicy.LocalAndWeb)]
        [InlineData("\"WebOnly\"", SourcePolicy.WebOnly)]
        [InlineData("\"localOnly\"", SourcePolicy.LocalOnly)]
        public void Read_ValidString_ReturnsExpectedPolicy(string json, SourcePolicy expected)
        {
            var policy = JsonSerializer.Deserialize<SourcePolicy>(json, _options);
            Assert.Equal(expected, policy);
        }

        [Theory]
        [InlineData("999")]
        [InlineData("\"InvalidPolicy\"")]
        public void Read_InvalidStringOrNumber_ThrowsJsonException(string json)
        {
            Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<SourcePolicy>(json, _options));
        }

        [Fact]
        public void Read_Null_ReturnsLocalOnly()
        {
            var policy = JsonSerializer.Deserialize<SourcePolicy>("null", _options);
            Assert.Equal(SourcePolicy.LocalOnly, policy);
        }
    }
}
