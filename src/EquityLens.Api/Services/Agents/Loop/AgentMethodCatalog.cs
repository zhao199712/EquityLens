using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace EquityLens.Api.Services.Agents.Loop;

public sealed record AgentMethod(string Id, string Name, string Description, string Role, string Version, string Content, IReadOnlyList<string>? AllowedTools = null)
{
    public string ContentHash => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Content))).ToLowerInvariant();
}

/// <summary>由部署檔案提供分析方法；方法只描述做法，不能增加角色工具或改變執行權限。</summary>
public sealed class AgentMethodCatalog(string directory)
{
    public IReadOnlyList<AgentMethod> ForRole(string role)
    {
        var methods = Directory.GetFiles(directory, "*.json").Order(StringComparer.Ordinal)
            .Select(path => JsonSerializer.Deserialize<AgentMethod>(File.ReadAllText(path), AgentNodeJson.SerializerOptions)
                ?? throw new InvalidOperationException("Invalid skill file."))
            .ToArray();
        if (methods.Any(x => string.IsNullOrWhiteSpace(x.Id) || string.IsNullOrWhiteSpace(x.Version) || string.IsNullOrWhiteSpace(x.Content)
            || x.Role is not ("Research" or "Portfolio")) || methods.Select(x => x.Id).Distinct().Count() != methods.Length)
            throw new InvalidOperationException("Invalid or duplicate skill metadata.");
        return methods.Where(x => x.Role == role).ToArray();
    }
}

/// <summary>角色限定的唯讀方法工具，使用既有持久化工具呼叫路徑。</summary>
internal sealed class SkillLoopTools(IAgentLoopTools inner, AgentMethodCatalog catalog, string role, int maxSkills) : IAgentLoopTools
{
    public const string LoadedKey = "loadedSkills";
    private static JsonObject Loaded(LoopToolScope scope) => scope.DomainContext?[LoadedKey] as JsonObject ?? new();
    public IReadOnlyList<LoopToolDefinition> Visible(LoopToolScope scope)
    {
        var restrictions = Loaded(scope).Select(x => x.Value?["allowedTools"]?.Deserialize<List<string>>(AgentNodeJson.SerializerOptions)).Where(x => x is not null).ToList();
        return [.. inner.Visible(scope).Where(x => restrictions.All(r => r!.Contains(x.Name))),
            new("load_skill", "載入本角色的一份分析方法；不是證據，不可擴大工具權限。", JsonNode.Parse("""{"type":"object","additionalProperties":false,"required":["id"],"properties":{"id":{"type":"string","maxLength":100}}}""")!.AsObject())];
    }
    public string? Validate(LoopToolScope scope, LoopToolCall call)
    {
        if (!Visible(scope).Any(x => x.Name == call.Tool)) return "ToolNotVisible";
        if (call.Tool != "load_skill") return inner.Validate(scope, call);
        try
        {
            if (call.Arguments.Count != 1 || call.Arguments["id"] is not JsonValue value || !value.TryGetValue<string>(out var id)) return "InvalidSkillArguments";
            if (catalog.ForRole(role).All(x => x.Id != id)) return "SkillNotVisible";
            if (!Loaded(scope).ContainsKey(id) && Loaded(scope).Count >= maxSkills) return "SkillLimitExceeded";
            return null;
        }
        catch (InvalidOperationException) { return "InvalidSkillArguments"; }
    }
    public Task<LoopToolResult> InvokeAsync(LoopToolScope scope, LoopToolCall call, CancellationToken ct)
    {
        var denied = Validate(scope, call);
        if (denied is not null) return Task.FromResult(new LoopToolResult([], "Tool denied", denied));
        if (call.Tool != "load_skill") return inner.InvokeAsync(scope, call, ct);
        var id = call.Arguments["id"]!.GetValue<string>();
        var skill = Loaded(scope)[id]?.Deserialize<AgentMethod>(AgentNodeJson.SerializerOptions) ?? catalog.ForRole(role).Single(x => x.Id == id);
        return Task.FromResult(new LoopToolResult([], $"Loaded skill {skill.Id} version {skill.Version}", Data: new() {
            ["skillId"] = skill.Id, ["version"] = skill.Version, ["contentHash"] = skill.ContentHash },
            BlackboardPatch: new() { [LoadedKey] = new JsonObject { [id] = JsonSerializer.SerializeToNode(skill, AgentNodeJson.SerializerOptions) } }));
    }
}
