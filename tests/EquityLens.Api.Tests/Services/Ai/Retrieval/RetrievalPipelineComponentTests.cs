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

    public class ResultRerankerTests
    {
        private readonly ResultReranker _reranker;

        public ResultRerankerTests()
        {
            _reranker = new ResultReranker(Options.Create(new RetrievalOptions()));
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

        private static RetrievedDocumentChunk CreateChunk(
            string content,
            double relevance,
            Guid? documentId = null,
            int? pageNumber = null)
        {
            return new RetrievedDocumentChunk(
                new DocumentSearchResult(
                    DocumentChunkId: Guid.NewGuid(),
                    DocumentId: documentId ?? Guid.NewGuid(),
                    DocumentTitle: "Test Document",
                    DocumentType: "AnnualReport",
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
                SourceRole: "Primary",
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
}
