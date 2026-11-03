using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using EquityLens.Api.Services.RiskAnalysis;

namespace EquityLens.Risk.IntegrationTests;

public sealed class RiskContractTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CSharpInput_PythonModelsAndEngines_CSharpResult(bool withAssets)
    {
        var input = Input(withAssets);
        var serialized = JsonSerializer.Serialize(input, Json);
        await using var python = new PythonProcess(["tests/EquityLens.Risk.IntegrationTests/python_contract.py"]);
        var result = await python.RunAsync(serialized);
        Assert.True(result.ExitCode == 0, result.Error);
        using var output = JsonDocument.Parse(result.Output);
        var root = output.RootElement;
        Assert.Equal(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(serialized))).ToLowerInvariant(), root.GetProperty("inputHash").GetString());
        var accepted = root.GetProperty("validatedInput");
        Assert.Equal(input.PortfolioId.ToString(), accepted.GetProperty("portfolioId").GetString());
        Assert.Equal(input.From.ToString("yyyy-MM-dd"), accepted.GetProperty("from").GetString());
        Assert.Equal(input.To.ToString("yyyy-MM-dd"), accepted.GetProperty("to").GetString());
        Assert.Equal(input.ReturnDates.Select(x => x.ToString("yyyy-MM-dd")), accepted.GetProperty("returnDates").EnumerateArray().Select(x => x.GetString()));
        Assert.Equal(input.Weights, accepted.GetProperty("weights").EnumerateArray().Select(x => x.GetDecimal()));
        Assert.Equal(input.AssetReturns[0], accepted.GetProperty("assetReturns")[0].EnumerateArray().Select(x => x.GetDecimal()));
        if (withAssets) Assert.Equal("台積電", accepted.GetProperty("assets")[0].GetProperty("securityName").GetString());
        else Assert.Equal(JsonValueKind.Null, accepted.GetProperty("assets").ValueKind);

        // Deserialize the real engine envelope and typed C# backtest response, not a hand-written result fixture.
        var backtest = root.GetProperty("backtest").Deserialize<RiskBacktestEngineResult>(Json)!;
        Assert.Equal(RiskEngineOutcomes.Success, backtest.Outcome);
        Assert.Null(backtest.ErrorCode);
        Assert.Null(backtest.ErrorMessage);
        Assert.Equal(input.PortfolioId, backtest.Value!.PortfolioId);
        Assert.Equal(input.From, backtest.Value.From);
        Assert.Equal(input.To, backtest.Value.To);
        Assert.Equal(input.LookbackDays, backtest.Value.LookbackDays);
        Assert.Equal(input.ReturnDates.Count - input.LookbackDays, backtest.Value.ObservationCount);
        Assert.NotEmpty(backtest.Value.Models);
        Assert.All(backtest.Value.Models, model =>
        {
            Assert.False(string.IsNullOrWhiteSpace(model.Model));
            Assert.Contains(model.ConfidenceLevel, input.ConfidenceLevels);
            Assert.Equal(backtest.Value.ObservationCount, model.ObservationCount);
            Assert.Equal(input.ReturnDates.Skip(input.LookbackDays), model.Points.Select(p => p.Date));
            Assert.All(model.Points, point => Assert.True(point.PredictedES <= point.PredictedVaR));
        });
        var risk = root.GetProperty("risk");
        Assert.Equal("Success", risk.GetProperty("outcome").GetString());
        Assert.Equal(JsonValueKind.Null, risk.GetProperty("errorCode").ValueKind);
        AssertCurrentRisk(risk.GetProperty("value"), input.PortfolioId);
        SaveArtifact($"contract-assets-{withAssets}-input.json", serialized);
        SaveArtifact($"contract-assets-{withAssets}-output.json", result.Output);
    }

    [Theory]
    [InlineData("fieldName")]
    [InlineData("matrixShape")]
    public async Task BrokenWireContract_IsRejectedByPython(string corruption)
    {
        var payload = JsonSerializer.SerializeToNode(Input(true), Json)!.AsObject();
        if (corruption == "fieldName") { payload["PortfolioId"] = payload["portfolioId"]!.DeepClone(); payload.Remove("portfolioId"); }
        else payload["weights"] = new JsonArray(1m);
        await using var python = new PythonProcess(["tests/EquityLens.Risk.IntegrationTests/python_contract.py"]);
        var result = await python.RunAsync(payload.ToJsonString());
        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("ValidationError", result.Error);
        Assert.Contains(corruption == "fieldName" ? "portfolioId" : "weights", result.Error);
    }

    internal static RiskBacktestEngineInput Input(bool assets)
    {
        var input = JsonSerializer.Deserialize<RiskBacktestEngineInput>(File.ReadAllText(Path.Combine(PythonProcess.Root, "tests/fixtures/risk_backtest_input.json")), Json)!;
        return input with { Assets = assets ? [new(Guid.Parse("11111111-1111-4111-8111-111111111111"), "2330", "TWSE", "台積電", "半導體", null), new(Guid.Parse("22222222-2222-4222-8222-222222222222"), "2317", "TWSE", "鴻海", "電子代工", "電子零組件")] : null };
    }

    internal static void AssertCurrentRisk(JsonElement value, Guid portfolioId)
    {
        Assert.Equal(portfolioId.ToString(), value.GetProperty("portfolioId").GetString());
        Assert.Equal("VT-GARCH-t + Joint-Vector FHS", value.GetProperty("selectedModel").GetString());
        Assert.Equal(0, value.GetProperty("fallbackDepth").GetInt32());
        Assert.True(value.GetProperty("fitHealth").GetProperty("healthy").GetBoolean());
        Assert.Equal(new[] { 1, 7, 30 }, value.GetProperty("horizons").EnumerateArray().Select(h => h.GetProperty("horizonDays").GetInt32()));
        foreach (var horizon in value.GetProperty("horizons").EnumerateArray())
        {
            Assert.Equal(2, horizon.GetProperty("confidenceLevels").GetArrayLength());
            foreach (var confidence in horizon.GetProperty("confidenceLevels").EnumerateArray())
            {
                var varValue = confidence.GetProperty("var").GetDouble();
                var es = confidence.GetProperty("expectedShortfall").GetDouble();
                Assert.True(double.IsFinite(varValue) && double.IsFinite(es));
                Assert.True(es <= varValue);
            }
        }
    }

    internal static void SaveArtifact(string name, string content)
    {
        var directory = Environment.GetEnvironmentVariable("RISK_TEST_ARTIFACTS");
        if (string.IsNullOrWhiteSpace(directory)) return;
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, name), content);
    }
}
