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

public sealed class PortfolioDiagnosisApprovalWorkflowTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task FixedDiagnosis_PausesWithoutLease_ResumesToDecisionBranch(bool approve)
    {
        await using var db = RefactorTestDbContext.Create();
        var run = new PortfolioDiagnosisWorkflowDefinitionProvider().CreateRun(Guid.NewGuid(), Guid.NewGuid());
        db.Add(run); await db.SaveChangesAsync();
        var executor = AgentExecutorTestFactory.Create(db, new WorkflowGraphTopologyService(), new AgentRunGraphValidator(),
            new AgentRunStateMachine(), new AgentNodeStateMachine(),
            [new Fixture("LoadPortfolioDiagnosisContext"), new Fixture("PortfolioAgentLoop"), new PortfolioQualityGateNodeHandler(),
             new WaitForHumanApprovalNodeHandler(), new FinalizePortfolioDiagnosisNodeHandler(), new FinalizeRejectedPortfolioDiagnosisNodeHandler()],
            NullLogger<AgentRunExecutor>.Instance, catalog: new AgentWorkflowCatalog());
        await executor.ExecuteAsync(run.Id, run.UserId);
        Assert.Equal(AgentRunStatuses.WaitingForFeedback, run.Status);
        Assert.Null(run.LeaseOwner); Assert.Null(run.LeaseExpiresAtUtc); Assert.Null(run.OutputJson);
        Assert.Empty(await db.AgentRunWakeOutbox.ToListAsync());
        Assert.Equal(6, run.Nodes.Count);
        var approval = await db.AgentApprovalRequests.SingleAsync();
        Assert.Equal("WorkflowGate", approval.Scope);
        var service = new AgentApprovalService(db, new AgentRunStateMachine(), new AgentNodeStateMachine());
        Assert.Null(await service.ApproveAsync(run.Id, approval.Id, Guid.NewGuid(), false, new(Guid.NewGuid(), "test")));
        await Assert.ThrowsAsync<AgentApprovalException>(() => service.RejectAsync(run.Id, approval.Id, run.UserId, false, new(Guid.NewGuid(), " ")));
        var decision = new DecideAgentApprovalRequest(Guid.NewGuid(), approve ? "核准" : "證據不足");
        if (approve) await service.ApproveAsync(run.Id, approval.Id, run.UserId, false, decision);
        else await service.RejectAsync(run.Id, approval.Id, run.UserId, false, decision);
        Assert.Single(await db.AgentRunWakeOutbox.ToListAsync());
        // requestId 重送不新增 wake。
        if (approve) await service.ApproveAsync(run.Id, approval.Id, run.UserId, false, decision);
        else await service.RejectAsync(run.Id, approval.Id, run.UserId, false, decision);
        Assert.Single(await db.AgentRunWakeOutbox.ToListAsync());
        await executor.ExecuteAsync(run.Id, run.UserId);
        Assert.Equal(approve ? AgentRunStatuses.Succeeded : AgentRunStatuses.Failed, run.Status);
        Assert.Equal(AgentNodeStatuses.Succeeded, run.Nodes.Single(n => n.NodeType == "PortfolioAgentLoop").Status);
        Assert.Equal(6, run.Nodes.Count);
        if (approve)
        {
            Assert.Equal(AgentNodeStatuses.Skipped, run.Nodes.Single(n => n.NodeType == PortfolioDiagnosisNodeTypes.FinalizeRejectedDiagnosis).Status);
            Assert.Contains("20%", run.OutputJson!);
        }
        else
        {
            Assert.Equal(AgentNodeStatuses.Skipped, run.Nodes.Single(n => n.NodeType == PortfolioDiagnosisNodeTypes.FinalizeDiagnosis).Status);
            Assert.Contains("證據不足", run.ErrorMessage!);
            Assert.True(JsonNode.Parse(run.OutputJson!)!["rejected"]!.GetValue<bool>());
        }
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
