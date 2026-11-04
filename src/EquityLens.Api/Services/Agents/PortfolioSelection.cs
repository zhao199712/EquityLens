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
}
