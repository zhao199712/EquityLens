using System.Text.Json.Nodes;
using EquityLens.Api.Data.Entities;
using EquityLens.Api.Services.CurrentUser;
using EquityLens.Api.Services.Agents;
using EquityLens.Api.Services.Agents.Loop;

namespace EquityLens.Api.Tests.Services.Agents;

public sealed class RiskProfileTests
{
    [Theory]
    [InlineData(3,"Conservative")][InlineData(4,"Conservative")][InlineData(5,"Conservative")]
    [InlineData(6,"Balanced")][InlineData(7,"Balanced")][InlineData(8,"Aggressive")][InlineData(9,"Aggressive")]
    public void ScoreBoundaries(int score, string level) => Assert.Equal(level, RiskQuestionnaire.Level(score));

    [Fact]
    public void AllAnswerCombinationsFollowScoringHardRulesAndLowerLevel()
    {
        string[] levels = ["Conservative", "Balanced", "Aggressive"];
        for (int c1=1;c1<=3;c1++) for (int c2=1;c2<=3;c2++) for (int c3=1;c3<=3;c3++)
        for (int w1=1;w1<=3;w1++) for (int w2=1;w2<=3;w2++) for (int w3=0;w3<=3;w3++)
        {
            var result = RiskQuestionnaire.Score(new() { C1=c1,C2=c2,C3=c3,W1=w1,W2=w2,W3=w3 });
            Assert.Equal(c1+c2+c3, result.CapacityScore);
            var expectedW = (int)Math.Floor((w1+w2+w3)*3.0/(w3==0 ? 2:3)+.5);
            Assert.Equal(expectedW, result.WillingnessScore);
            Assert.Equal(c1==1 || c2==1 ? "Conservative" : RiskQuestionnaire.Level(c1+c2+c3),result.CapacityLevel);
            Assert.Equal(RiskQuestionnaire.Level(expectedW),result.WillingnessLevel);
            Assert.Equal(levels[Math.Min(Array.IndexOf(levels,result.CapacityLevel),Array.IndexOf(levels,result.WillingnessLevel))],result.Level);
        }
    }

    [Fact]
    public void StaleBeginsAfterTwelveCalendarMonths()
    {
        var at = new DateTime(2024,2,29,0,0,0,DateTimeKind.Utc);
        var row = new UserRiskProfile { Level="Balanced", AssessedAtUtc=at, Source="Questionnaire" };
        Assert.False(RiskPreferenceProfile.FromProfile(row,null,at.AddMonths(12)).Stale);
        var stale = RiskPreferenceProfile.FromProfile(row,null,at.AddMonths(12).AddTicks(1));
        Assert.True(stale.Stale); Assert.Contains("可能過時",stale.Statement);
        Assert.False(RiskPreferenceProfile.FromProfile(null,null,at).Stale);
    }

    private static readonly PortfolioDiagnosisContext Period = new(Guid.NewGuid(),"Synthetic","TWD",new(2025,1,1),new(2025,12,31),3);
    private static JsonArray Results() => JsonNode.Parse("""[{"operation":"calculate-annualized-volatility","value":0.5887,"provenance":{"measurementBasis":{"from":"2025-01-01","to":"2025-12-31","weighting":"fixed shares","cashTreatment":"excluded","observationCount":252}}},{"operation":"calculate-max-drawdown","value":-0.4726,"provenance":{"measurementBasis":{"from":"2025-01-01","to":"2025-12-31","weighting":"fixed shares","cashTreatment":"excluded","observationCount":252}}}]""")!.AsArray();

    [Fact]
    public void FitUsesExactAnalysisPeriodAndBasis_WithUnavailableWhenAbsentOrUnapproved()
    {
        var profile = RiskPreferenceProfile.FromSaved(null);
        var approved = new RiskThresholdDefinition("v1",true);
        var data = RiskProfileEvidence.Create(profile,Results(),Period,approved);
        var fit = data["profileFit"]!.AsArray();
        Assert.Equal(3.27m,fit[0]!["ratio"]!.GetValue<decimal>()); Assert.Equal("Above",fit[0]!["status"]!.ToString());
        Assert.Equal("Below",fit[1]!["status"]!.ToString()); Assert.Null(fit[1]!["ratio"]);
        Assert.Equal("2025-01-01",fit[0]!["period"]!["from"]!.ToString());
        Assert.Equal("fixed shares",fit[0]!["basis"]!["weighting"]!.ToString());
        foreach (var unavailable in new[] {
            RiskProfileEvidence.Create(profile,null,Period,approved),
            RiskProfileEvidence.Create(profile,Results(),Period with { From=new(2024,1,1) },approved),
            RiskProfileEvidence.Create(profile,Results(),Period,new("v1",false)) })
            Assert.All(unavailable["profileFit"]!.AsArray(), x => { Assert.Equal("Unavailable",x!["status"]!.ToString()); Assert.Null(x["ratio"]); Assert.Null(x["value"]); });
        Assert.Null(RiskProfileEvidence.Create(profile,Results(),Period,new("v1",false))["thresholds"]);
    }

    [Fact]
    public void FitUsesFrozenTradingDatesInsteadOfCalendarRequestWindow()
    {
        var requested = Period with { From = new(2024,12,29), To = new(2026,1,4) };
        var inputs = new PortfolioRiskMathInputs("Portfolio",Period.PortfolioId,null,Period.From,Period.To,[],[],[],[],[],[],[],[],252,"synthetic",[]);
        var fit = RiskProfileEvidence.Create(RiskPreferenceProfile.FromSaved(null),Results(),requested,new("v1",true),inputs)["profileFit"]!.AsArray();
        Assert.Equal("Above",fit[0]!["status"]!.ToString());
        Assert.Equal("2025-01-01",fit[0]!["period"]!["from"]!.ToString());
        Assert.Equal(3.27m,fit[0]!["ratio"]!.GetValue<decimal>());
        var mismatch = RiskProfileEvidence.Create(RiskPreferenceProfile.FromSaved(null),Results(),requested,new("v1",true),inputs with { From=new(2024,12,30) })["profileFit"]!.AsArray();
        Assert.All(mismatch, f => Assert.Equal("Unavailable",f!["status"]!.ToString()));
    }

    [Fact]
    public void LaterAnalysesPreserveCoreEvidence_AndGateRejectsStaleUnavailableClaim()
    {
        var core = Results();
        var tail = new JsonArray(new JsonObject { ["operation"]="calculate-historical-var", ["value"]=-.02m });
        var merged = PortfolioLoopTools.MergeProfileResults(core,tail);
        Assert.Equal(2,merged.Count); Assert.Equal(core.ToJsonString(),merged.ToJsonString());
        var newer = Results(); newer[0]!["value"] = .36m;
        Assert.Equal(.36m,PortfolioLoopTools.MergeProfileResults(merged,newer)[0]!["value"]!.GetValue<decimal>());
        var ready = RiskProfileEvidence.Create(RiskPreferenceProfile.FromSaved(null),merged,Period,new("v1",true));
        var notYet = RiskProfileEvidence.Create(RiskPreferenceProfile.FromSaved(null),null,Period,new("v1",true));
        var facts = new[] { new LoopDataFact("P1","before","get_risk_profile",notYet),new LoopDataFact("P2","after","run_risk_analysis",new() { ["riskProfile"]=ready }) };
        var answer = new LoopAnswer("profileFit 因風險分析當時不可用而為 Unavailable [P1]",["P1"]);
        Assert.Contains("StaleRiskProfileComparison",PortfolioQualityGateNodeHandler.Check(answer,facts));
        Assert.Contains("[P2]",PortfolioQualityGateNodeHandler.RepairHints([],facts,answer));
        Assert.Empty(PortfolioQualityGateNodeHandler.Check(new("年化波動為參考上限的 3.27 倍 [P2]",["P2"]),facts));
    }

    [Fact]
    public void MixedProfileAndScenarioParagraph_GivesActionableRepairWithoutWeakeningGate()
    {
        var profile = RiskProfileEvidence.Create(RiskPreferenceProfile.FromSaved(null),Results(),Period,new("v1",true));
        var facts = new[] { new LoopDataFact("P1","core","run_risk_analysis",new() { ["riskProfile"]=profile }),
            new LoopDataFact("P2","scenario","simulate_weight_scenario",new() { ["status"]="Calculated" }) };
        const string current = "現況年化波動 58.87%，高於參考上限 18% [P1]。";
        const string scenario = "另外比較減碼留現金情境 [P2]。";
        var bad = new LoopAnswer(current+scenario,["P1","P2"]);
        Assert.Contains("MixedRiskBasis",PortfolioQualityGateNodeHandler.Check(bad,facts));
        var hint = PortfolioQualityGateNodeHandler.RepairHints([],facts,bad);
        Assert.Contains("換行分成獨立段落",hint);
        Assert.Contains("profileFit",hint);
        Assert.DoesNotContain("風險偏好不能支持風險數字",hint);
        Assert.Empty(PortfolioQualityGateNodeHandler.Check(new(current+"\n\n"+scenario,["P1","P2"]),facts));
        Assert.Contains("UntraceableNumbers",PortfolioQualityGateNodeHandler.Check(new(current.Replace("58.87","59.87")+"\n\n"+scenario,["P1","P2"]),facts));
    }

    [Fact]
    public void GateBindsEachThresholdToItsMetric_WithoutInventingUnavailableComparison()
    {
        var data = RiskProfileEvidence.Create(RiskPreferenceProfile.FromSaved(null),null,Period,new("v1",true));
        var facts = new[] { new LoopDataFact("P1","profile","get_risk_profile",data) };
        Assert.Empty(PortfolioQualityGateNodeHandler.Check(new("年化波動參考上限 18%；最大回撤參考下限 -25% [P1]",["P1"]),facts));
        Assert.Contains("UnverifiedProfileFit",PortfolioQualityGateNodeHandler.Check(new("年化波動參考上限 -25% [P1]",["P1"]),facts));
        Assert.Contains("UnverifiedProfileFit",PortfolioQualityGateNodeHandler.Check(new("年化波動為參考上限的 1.36 倍 [P1]",["P1"]),facts));
    }

    [Fact]
    public void ProfileStatementIsRenderedFromAnalysisEvidenceWithoutSeparateProfileCall()
    {
        var profile=RiskProfileEvidence.Create(RiskPreferenceProfile.FromSaved(null),Results(),Period);
        var facts=new[] { new LoopDataFact("P2","core","run_risk_analysis",new() { ["riskProfile"]=profile }) };
        var output=PortfolioQualityGateNodeHandler.WithRiskPreference("風險摘要",facts)!;
        Assert.Contains("依預設的風險偏好",output);
        Assert.Contains("可在設定中完成評估",output);
        Assert.Contains("[P2]",output);
        Assert.Equal(output,PortfolioQualityGateNodeHandler.WithRiskPreference(output,facts));
    }

    [Theory]
    [InlineData(false)][InlineData(true)]
    public void ProfileThresholdRelationUsesSavedStatusRatherThanScenarioComparison(bool hasScenario)
    {
        var profile=RiskProfileEvidence.Create(RiskPreferenceProfile.FromSaved(null),Results(),Period);
        var facts=new List<LoopDataFact> { new("P1","core","run_risk_analysis",new() { ["riskProfile"]=profile }) };
        if (hasScenario) facts.Add(new("P2","scenario","simulate_weight_scenario",new() { ["status"]="Calculated" }));
        Assert.Empty(PortfolioQualityGateNodeHandler.Check(new("年化波動高於產品參考上限 [P1]",["P1"]),facts));
        Assert.Empty(PortfolioQualityGateNodeHandler.Check(new("年化波動高於產品參考上限18.00% [P1]",["P1"]),facts));
        Assert.Contains("UnverifiedProfileFit",PortfolioQualityGateNodeHandler.Check(new("年化波動高於產品參考上限10.00% [P1]",["P1"]),facts));
        Assert.Empty(PortfolioQualityGateNodeHandler.Check(new("最大回撤低於產品參考下限 [P1]",["P1"]),facts));
        Assert.Contains("UnverifiedProfileFit",PortfolioQualityGateNodeHandler.Check(new("年化波動低於產品參考上限 [P1]",["P1"]),facts));
        Assert.Contains("UnverifiedProfileFit",PortfolioQualityGateNodeHandler.Check(new("年化波動高於產品參考上限",[]),facts));
        Assert.Contains("UnverifiedProfileFit",PortfolioQualityGateNodeHandler.Check(new("年化波動高於產品參考下限 [P1]",["P1"]),facts));
    }

    [Fact]
    public void GateRejectsInventedRatioEvenIfNumberAppearsElsewhere_AndDefaultMisrepresentation()
    {
        var data = RiskProfileEvidence.Create(RiskPreferenceProfile.FromSaved(null),Results(),Period,new("v1",true));
        data["unrelated"] = 4.2m;
        var facts = new[] { new LoopDataFact("P1","profile","get_risk_profile",data) };
        Assert.Empty(PortfolioQualityGateNodeHandler.Check(new("年化波動為參考上限的 3.27 倍 [P1]",["P1"]),facts));
        Assert.Contains("UnverifiedProfileFit",PortfolioQualityGateNodeHandler.Check(new("年化波動為參考上限的 4.2 倍 [P1]",["P1"]),facts));
        Assert.Contains("DefaultPreferenceMisrepresented",PortfolioQualityGateNodeHandler.Check(new("您的風險偏好為穩健 [P1]",["P1"]),facts));
        Assert.Empty(PortfolioQualityGateNodeHandler.Check(new(data["statement"]!.ToString()+" [P1]",["P1"]),facts));
        data["unrelatedThreshold"] = .10m;
        Assert.Contains("UnverifiedProfileFit",PortfolioQualityGateNodeHandler.Check(new("年化波動參考上限 10% [P1]",["P1"]),facts));
        Assert.Empty(PortfolioQualityGateNodeHandler.Check(new("年化波動參考上限 18% [P1]",["P1"]),facts));
        var defaultOutput = PortfolioQualityGateNodeHandler.WithRiskPreference("工具摘要",facts)!;
        Assert.Contains("預設",defaultOutput); Assert.Contains("可在設定中完成評估",defaultOutput);
    }
}
