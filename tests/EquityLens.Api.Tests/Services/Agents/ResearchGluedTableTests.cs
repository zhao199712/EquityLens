using System.Text.Json.Nodes;
using EquityLens.Api.Services.Agents.Loop;

namespace EquityLens.Api.Tests.Services.Agents;

/// <summary>正式站回報：法說會表格欄位黏連與季度代號，使正確答案被整份改為資料不足。</summary>
public sealed class ResearchGluedTableTests
{
    private const string Income = "綜合損益表項目1Q261Q264Q251Q25季變化年變化(除另予註明者外，金額為新台幣十億元)業績展望營業收入淨額(美金十億元)35.9034.6-35.833.7325.53+6.4%+40.6%營業收入淨額1,134.101,046.09839.25+8.4%+35.1%營業毛利率66.2%63.0%-65.0%62.3%58.8%+3.9 ppts+7.4 ppts歸屬予母公司業主之本期淨利572.48505.74361.56+13.2%+58.3%每股盈餘(新台幣元)22.0819.5013.94+13.2%+58.3%";
    private const string Outlook = "2026年第二季業績展望基於目前對營運展望的假設，台積公司預期：•合併營收約介於美金390億元到402億元之間。此外，基於平均匯率31.7的假設";

    private static JsonObject[] Citations() =>
    [
        new() { ["citationIndex"] = 1, ["quoteText"] = Income },
        new() { ["citationIndex"] = 3, ["quoteText"] = Outlook },
    ];

    [Fact]
    public void Production_answer_with_glued_columns_and_quarter_codes_is_not_blocked()
    {
        const string answer = """
            **最新財報重點（2026年第一季，法說會簡報）**
            - 1Q26 營業收入淨額為新台幣 1,134.10 十億元，較 4Q25 的 1,046.09 十億元季增 8.4%，較 1Q25 的 839.25 十億元年增 35.1% [1]。
            - 1Q26 營業毛利率 66.2%，較 4Q25 的 63.0% 增加 3.9 個百分點，較 1Q25 的 58.8% 增加 7.4 個百分點 [1]。
            - 1Q26 歸屬母公司業主本期淨利 572.48 十億元，季增 13.2%、年增 58.3%；每股盈餘 22.08 元 [1]。
            - 台積公司預期 2Q26 合併營收約介於美金 390 億元到 402 億元之間 [3]。
            """;
        var result = ResearchNumberTraceability.Check(answer, Citations(), "台積電最新的財報 對我的投資組合有影響嗎", "2330");
        Assert.False(result.Blocking, string.Join(" | ", result.Warnings));
        Assert.DoesNotContain(result.Warnings, w => w.StartsWith("CheckNumber: 26") || w == "CheckNumber: 2 (plain)");
    }

    [Theory]
    [InlineData("4Q25 營收為 1,200.00 十億元 [1]。")]     // 原文沒有這個數字（金額既有 0.5% 容差外）
    [InlineData("4Q25 營收為 1,046.09 億元 [1]。")]       // 表頭是十億元，單位差 10 倍
    [InlineData("4Q25 營收為 1,046.09 十億元。")]          // 沒有引用
    public void Split_glued_columns_still_reject_wrong_values_units_or_missing_citations(string answer) =>
        Assert.True(ResearchNumberTraceability.Check(answer, Citations(), null, "2330").Blocking);
}

public sealed class ResearchPeriodTokenTests
{
    [Theory]
    [InlineData("1Q26 毛利率 66.2% [1]。")]
    [InlineData("Q2 2026 毛利率 66.2% [1]。")]
    [InlineData("2026Q1 毛利率 66.2% [1]。")]
    [InlineData("第一季毛利率 66.2% [1]。")]
    public void Quarter_codes_are_period_names_not_numbers(string answer)
    {
        var citations = new[] { new JsonObject { ["citationIndex"] = 1, ["quoteText"] = "營業毛利率66.2%63.0%" } };
        var result = ResearchNumberTraceability.Check(answer, citations);
        Assert.False(result.Blocking, string.Join(" | ", result.Warnings));
        Assert.DoesNotContain(result.Warnings, w => w.StartsWith("CheckNumber"));
    }
}
