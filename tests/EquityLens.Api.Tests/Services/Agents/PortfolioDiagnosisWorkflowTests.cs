using System.Text.Json;
using System.Text.Json.Nodes;
using EquityLens.Api.Contracts.Agents;
using EquityLens.Api.Data;
using EquityLens.Api.Data.Entities;
using EquityLens.Api.Services.Agents;
using EquityLens.Api.Services.Agents.Loop;
using EquityLens.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace EquityLens.Api.Tests.Services.Agents;

public sealed class PortfolioDiagnosisWorkflowTests
{
    [Fact]
    public async Task FixedDiagnosis_RunsToCompletionWithoutHumanApprovalGate()
    {
        await using var db = RefactorTestDbContext.Create();
        var run = new PortfolioDiagnosisWorkflowDefinitionProvider().CreateRun(Guid.NewGuid(), Guid.NewGuid());
        Assert.Equal(5, JsonNode.Parse(run.WorkflowDefinitionJson)!["version"]!.GetValue<int>());
        Assert.Equal(["LoadPortfolioDiagnosisContext", "PortfolioAgentLoop", "PortfolioQualityGate", PortfolioDiagnosisNodeTypes.FinalizeDiagnosis],
            run.Nodes.Select(n => n.NodeType));
        db.Add(run); await db.SaveChangesAsync();
        var executor = AgentExecutorTestFactory.Create(db, new WorkflowGraphTopologyService(), new AgentRunGraphValidator(),
            new AgentRunStateMachine(), new AgentNodeStateMachine(),
            [new Fixture("LoadPortfolioDiagnosisContext"), new Fixture("PortfolioAgentLoop"), new PortfolioQualityGateNodeHandler(),
             new FinalizePortfolioDiagnosisNodeHandler()],
            NullLogger<AgentRunExecutor>.Instance, catalog: new AgentWorkflowCatalog());
        await executor.ExecuteAsync(run.Id, run.UserId);
        Assert.Equal(AgentRunStatuses.Succeeded, run.Status);
        Assert.All(run.Nodes, n => Assert.Equal(AgentNodeStatuses.Succeeded, n.Status));
        Assert.Empty(await db.AgentApprovalRequests.ToListAsync());
        Assert.Contains("20%", run.OutputJson!);
    }

    [Theory]
    [InlineData("波動20%[P1]。", false)]
    [InlineData("波動99%[P1]。", true)]
    [InlineData("波動20%[P9]。", true)]
    [InlineData("波動20%。", true)]
    public void PortfolioGate_RejectsNumbersWithoutMatchingToolEvidence(string answer, bool blocked)
    {
        var issues = PortfolioQualityGateNodeHandler.Check(new(answer, ["P1"]),
            [new("P1", "key", "get_risk_report", new() { ["annualizedVolatility"] = .2m })]);
        Assert.Equal(blocked, issues.Count > 0);
    }

    [Theory]
    [InlineData(180, "OptimizationScenarioRequired")]
    [InlineData(20, "OptimizationDataUnavailable")]
    public async Task OptimizationWithoutScenario_CannotPassQualityGate(int days, string expected)
    {
        await using var db = RefactorTestDbContext.Create();
        var board = new JsonObject {
            [AgentBlackboardKeys.PortfolioContext] = JsonSerializer.SerializeToNode(new PortfolioDiagnosisContext(Guid.NewGuid(), "測試", "TWD", new(2025, 1, 1), new(2025, 12, 31), 1), AgentNodeJson.SerializerOptions),
            [AgentBlackboardKeys.RoutingContext] = new JsonObject { ["objective"] = "如何優化配置降低波動？" },
            ["portfolioLoopAnswer"] = JsonSerializer.SerializeToNode(new LoopAnswer("建議降低集中度[P1]。", ["P1"]), AgentNodeJson.SerializerOptions),
            ["agentLoopFacts"] = JsonSerializer.SerializeToNode(new[] { new LoopDataFact("P1", "key", "get_risk_report", new() { ["volatility"] = .2m }) }, AgentNodeJson.SerializerOptions),
            [AgentBlackboardKeys.MathInputs] = new JsonObject { ["commonTradingDays"] = days }
        };
        var run = new AgentRun { Id = Guid.NewGuid(), BlackboardJson = board.ToJsonString() };
        await new PortfolioQualityGateNodeHandler().ExecuteAsync(new(db, run, new AgentRunNode(), (_, _, _, _, _) => { }));
        var output = JsonNode.Parse(run.BlackboardJson)!;
        Assert.False(output["portfolioQualityGate"]!["passed"]!.GetValue<bool>());
        Assert.Contains(expected, output["portfolioQualityGate"]!["blockingIssues"]!.ToJsonString());
        Assert.Null(output[AgentBlackboardKeys.PortfolioDiagnosisDraft]!["interpretation"]);
    }

    [Theory]
    [InlineData(true, true, true)]
    [InlineData(true, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, true, false)]
    [InlineData(true, false, false)]
    public async Task Report_DisclosesOnlyAvailableSavedPreference(bool available, bool isDefault, bool rejected)
    {
        await using var db = RefactorTestDbContext.Create();
        var statement = isDefault ? "依你的風險偏好『穩健』（預設，可在設定修改）" : "依你的風險偏好『積極』（已保存，可在設定修改）";
        var board = new JsonObject {
            [AgentBlackboardKeys.PortfolioContext] = JsonSerializer.SerializeToNode(new PortfolioDiagnosisContext(Guid.NewGuid(), "測試", "TWD", new(2025, 1, 1), new(2025, 12, 31), 1), AgentNodeJson.SerializerOptions),
            ["portfolioLoopAnswer"] = JsonSerializer.SerializeToNode(new LoopAnswer(rejected ? "波動99%。" : "工具資料已取得[P1]。", ["P1"]), AgentNodeJson.SerializerOptions),
            ["agentLoopFacts"] = JsonSerializer.SerializeToNode(new[] { new LoopDataFact("P1", "key", "get_risk_profile", new() { ["available"] = available, ["statement"] = statement, ["isDefault"] = isDefault }) }, AgentNodeJson.SerializerOptions)
        };
        var run = new AgentRun { Id = Guid.NewGuid(), BlackboardJson = board.ToJsonString() };
        await new PortfolioQualityGateNodeHandler().ExecuteAsync(new(db, run, new AgentRunNode(), (_, _, _, _, _) => { }));
        var output = JsonNode.Parse(run.BlackboardJson)!;
        Assert.Equal(!rejected, output["portfolioQualityGate"]!["passed"]!.GetValue<bool>());
        if (rejected) Assert.Contains("UntraceableNumbers", output["portfolioQualityGate"]!["blockingIssues"]!.ToJsonString());
        var summary = output[AgentBlackboardKeys.PortfolioDiagnosisDraft]!["summary"]!.ToString();
        Assert.Equal(available, summary.Contains(statement + " [P1]"));
        if (rejected) Assert.Null(output[AgentBlackboardKeys.PortfolioDiagnosisDraft]!["interpretation"]);
        else Assert.Contains(statement + " [P1]", output[AgentBlackboardKeys.PortfolioDiagnosisDraft]!["interpretation"]!.ToString());
    }

    [Fact]
    public void SavedFourteenAnswers_DisclosureDoesNotChangeNumericOrCitationVerdicts()
    {
        var rows = JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,
            "Services/Agents/Fixtures/round4-preference-disclosure.json")))!.AsArray();
        Assert.Equal(14, rows.Count);
        var rendered = new JsonArray();
        foreach (var row in rows)
        {
            var text = row!["answer"]!.ToString();
            var facts = row["facts"]!.Deserialize<List<LoopDataFact>>(AgentNodeJson.SerializerOptions)!;
            var result = PortfolioQualityGateNodeHandler.WithRiskPreference(text, facts)!;
            string[] Ids(string value) => System.Text.RegularExpressions.Regex.Matches(value, @"\[(P\d+)\]")
                .Select(m => m.Groups[1].Value).Distinct().ToArray();
            Assert.Equal(PortfolioQualityGateNodeHandler.Check(new(text, Ids(text)), facts),
                PortfolioQualityGateNodeHandler.Check(new(result, Ids(result)), facts));
            Assert.Equal(result, PortfolioQualityGateNodeHandler.WithRiskPreference(result, facts));
            foreach (var profile in facts.Where(f => f.Tool == "get_risk_profile" && f.Data["available"]?.ToString() == "true"))
                Assert.Contains(profile.Data["statement"]!.ToString(), result);
            rendered.Add(new JsonObject { ["id"] = row["id"]!.DeepClone(), ["answer"] = result });
        }
        // 選用驗收輸出：由實際 production renderer 產生，供既有 scorer 離線重算，不呼叫模型。
        if (Environment.GetEnvironmentVariable("EQUITYLENS_EVAL_REPLAY_OUTPUT") is { Length: > 0 } output)
            File.WriteAllText(output, rendered.ToJsonString(AgentNodeJson.SerializerOptions));
    }

    private sealed class Fixture(string type) : IAgentNodeHandler
    {
        public string NodeType => type;
        public Task ExecuteAsync(AgentNodeExecutionContext context, CancellationToken cancellationToken = default)
        {
            var board = AgentNodeJson.ParseBlackboard(context.Run.BlackboardJson);
            if (type == "LoadPortfolioDiagnosisContext")
                board[AgentBlackboardKeys.PortfolioContext] = JsonSerializer.SerializeToNode(new PortfolioDiagnosisContext(Guid.NewGuid(), "測試", "TWD", new(2025, 1, 1), new(2025, 12, 31), 1), AgentNodeJson.SerializerOptions);
            else
            {
                board["portfolioLoopAnswer"] = JsonSerializer.SerializeToNode(new LoopAnswer("波動20%[P1]。", ["P1"]), AgentNodeJson.SerializerOptions);
                board["agentLoopFacts"] = JsonSerializer.SerializeToNode(new[] { new LoopDataFact("P1", "key", "get_risk_report", new() { ["annualizedVolatility"] = .2m }) }, AgentNodeJson.SerializerOptions);
            }
            context.Run.BlackboardJson = board.ToJsonString(AgentNodeJson.SerializerOptions);
            return Task.CompletedTask;
        }
    }
}
