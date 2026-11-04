using System.Text.Json;

namespace EquityLens.Api.Tests.Services.Agents;

public sealed class ProductionLoggingTests
{
    [Fact]
    public void ProductionConfiguration_SuppressesSqlInformationLogs()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "AGENTS.md"))) root = root.Parent;
        Assert.NotNull(root);
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(root.FullName, "src/EquityLens.Api/appsettings.Production.json")));
        Assert.True(doc.RootElement.TryGetProperty("Logging", out var logging));
        Assert.Equal("Warning", logging.GetProperty("LogLevel").GetProperty("Microsoft.EntityFrameworkCore.Database.Command").GetString());
    }
}
