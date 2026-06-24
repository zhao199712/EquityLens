using System.Text.Json;
using System.Text.Json.Serialization;
using EquityLens.Api.Contracts.Research;
using EquityLens.Api.Services.Ai;
using EquityLens.Api.Services.Ai.Retrieval;
using Microsoft.Extensions.Options;

namespace EquityLens.Api.Tests.Services.Ai.Retrieval;

public sealed class RetrievalRegressionDatasetTests
{
    private readonly IntentDetector _intentDetector = new();
    private readonly RetrievalPlanner _planner;

    public RetrievalRegressionDatasetTests()
    {
        _planner = new RetrievalPlanner(Options.Create(new RetrievalOptions()));
    }

    [Fact]
    public void DatasetFile_IsWellFormedAndHasCases()
    {
        var dataset = LoadDataset();
        Assert.NotNull(dataset);
        Assert.NotEmpty(dataset.Cases);
        Assert.All(dataset.Cases, c => Assert.False(string.IsNullOrWhiteSpace(c.Id)));
    }

        [Theory]
        [MemberData(nameof(GetCases))]
        public void IntentDetector_MatchesExpectedIntent(RegressionCase testCase)
        {
            Assert.NotNull(testCase.ExpectedIntent);
            var result = _intentDetector.Detect(testCase.Question);
            Assert.Equal(Enum.Parse<ResearchQuestionIntent>(testCase.ExpectedIntent!), result.Selected);
        }

    [Theory]
    [MemberData(nameof(GetCasesWithDocumentType))]
    public void RetrievalPlanner_MatchesExpectedPrimaryDocumentType(RegressionCase testCase)
    {
        var mode = string.IsNullOrWhiteSpace(testCase.RetrievalMode)
            ? (RetrievalMode?)null
            : Enum.Parse<RetrievalMode>(testCase.RetrievalMode);

        var plan = _planner.BuildPlan(testCase.Question, mode, documentType: null, topK: 10);
        var primarySearch = plan.Searches.FirstOrDefault(s => s.SourceRole == "Primary");
        Assert.NotNull(primarySearch);
        Assert.Equal(testCase.ExpectedPrimaryDocumentType, primarySearch.DocumentType);
    }

    public static IEnumerable<object[]> GetCases()
    {
        var dataset = LoadDataset();
        return dataset.Cases.Where(c => !string.IsNullOrWhiteSpace(c.ExpectedIntent)).Select(c => new object[] { c });
    }

    public static IEnumerable<object[]> GetCasesWithDocumentType()
    {
        var dataset = LoadDataset();
        return dataset.Cases.Where(c => !string.IsNullOrWhiteSpace(c.ExpectedPrimaryDocumentType)).Select(c => new object[] { c });
    }

    private static RegressionDataset LoadDataset()
    {
        var path = Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "Fixtures",
            "RetrievalRegression",
            "regression-dataset.json");
        var fullPath = Path.GetFullPath(path);
        var json = File.ReadAllText(fullPath);
        return JsonSerializer.Deserialize<RegressionDataset>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            NumberHandling = JsonNumberHandling.AllowReadingFromString
        })!;
    }

    public sealed class RegressionDataset
    {
        public required string Version { get; set; }
        public required string Description { get; set; }
        public required List<RegressionCase> Cases { get; set; }
    }

    public sealed class RegressionCase
    {
        public required string Id { get; set; }
        public required string Question { get; set; }
        public required string Ticker { get; set; }
        public string? RetrievalMode { get; set; }
        public string? ExpectedIntent { get; set; }
        public string? ExpectedPrimaryDocumentType { get; set; }
        public List<string> ExpectedEvidenceTerms { get; set; } = [];
        public int? MinExpectedCitations { get; set; }
        public string? ExpectedStatus { get; set; }
    }
}
