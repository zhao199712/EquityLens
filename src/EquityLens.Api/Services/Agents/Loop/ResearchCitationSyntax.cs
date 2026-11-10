using System.Text.RegularExpressions;

namespace EquityLens.Api.Services.Agents.Loop;

/// <summary>只統一明確的證據標記；未知 ID 仍由 Ledger 與品質關卡拒絕，不補造引用。</summary>
internal static partial class ResearchCitationSyntax
{
    internal static string Normalize(string markdown) => Group().Replace(markdown, m =>
        string.Concat(Id().Matches(m.Groups["ids"].Value).Select(x => $"[{x.Value}]")));

    [GeneratedRegex(@"(?:\[(?<ids>E\d+(?:\s*[,、，]\s*E\d+)*)\]|【(?<ids>E\d+(?:\s*[,、，]\s*E\d+)*)】|〔(?<ids>E\d+(?:\s*[,、，]\s*E\d+)*)〕|［(?<ids>E\d+(?:\s*[,、，]\s*E\d+)*)］)")]
    private static partial Regex Group();
    [GeneratedRegex(@"E\d+")]
    private static partial Regex Id();
}
