namespace EquityLens.Api.Services.Agents;

/// <summary>只以使用者原文選取完整名稱或代碼；較長的完整名稱優先，避免名稱互為子字串時選錯。</summary>
internal static class PortfolioSelection
{
    internal static IEnumerable<Guid> Named(string message, IEnumerable<(Guid Id, string Name)> options)
    {
        var candidates = options.ToArray();
        var matched = candidates.Where(p => !string.IsNullOrWhiteSpace(p.Name) && message.Contains(p.Name, StringComparison.OrdinalIgnoreCase)).ToArray();
        return candidates.Where(p => message.Contains(p.Id.ToString(), StringComparison.OrdinalIgnoreCase)
            || matched.Any(m => m.Id == p.Id) && !matched.Any(other => other.Name.Length > p.Name.Length && other.Name.Contains(p.Name, StringComparison.OrdinalIgnoreCase)))
            .Select(p => p.Id).Distinct();
    }

    /// <summary>
    /// 使用者只說出名稱片段（如「高波動的」）時，僅接受唯一對應單一投組、至少三個字的片段；
    /// 先移除「投資組合」等通用字，避免「優化投資組合」誤選名稱含這些字的投組。
    /// </summary>
    internal static IEnumerable<Guid> NamedOrPartial(string message, IEnumerable<(Guid Id, string Name)> options)
    {
        var candidates = options.ToArray();
        var named = Named(message, candidates).ToArray();
        if (named.Length > 0) return named;
        var partial = candidates.Where(p => Fragments(p.Name).Any(f => message.Contains(f, StringComparison.OrdinalIgnoreCase)
                && !candidates.Any(o => o.Id != p.Id && o.Name.Contains(f, StringComparison.OrdinalIgnoreCase))))
            .Select(p => p.Id).Distinct().ToArray();
        return partial.Length == 1 ? partial : [];
    }

    private static IEnumerable<string> Fragments(string name)
    {
        var core = System.Text.RegularExpressions.Regex.Replace(name ?? "", @"投資組合|組合|投組|portfolio", "",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase).Trim();
        for (var length = core.Length; length >= 3; length--)
            for (var start = 0; start + length <= core.Length; start++)
                if (!string.IsNullOrWhiteSpace(core.Substring(start, length))) yield return core.Substring(start, length);
    }
}
