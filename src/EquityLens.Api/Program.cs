using System.Text;
using EquityLens.Api.Bootstrap;
using EquityLens.Api.Services.Agents;

Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

var builder = WebApplication.CreateBuilder(args);
builder.Host.UseDefaultServiceProvider(o => o.ValidateOnBuild = false);
var e2eTestMode = string.Equals(builder.Configuration["E2E_TEST_MODE"], "1", StringComparison.Ordinal);

builder.AddEquityLensObservability();
builder.AddEquityLensInfrastructure();
builder.AddEquityLensPortfolios();
builder.AddEquityLensDocuments();
builder.AddEquityLensAgents(e2eTestMode);
builder.AddEquityLensResearchAi();
builder.AddEquityLensImportsAndMarketData();
builder.AddEquityLensHttp();

var app = builder.Build();

// Persisted prompt bindings must be initialized before web or CLI execution.
using (var scope = app.Services.CreateScope())
{
    await scope.ServiceProvider.GetRequiredService<PromptManagementBootstrapService>().EnsureSeededAsync();
}

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors("AllowFrontend");
app.UseAuthentication();
app.UseAuthorization();
app.MapGet("/healthz", () => Results.Ok(new { status = "ok" }));
app.MapControllers();
if (e2eTestMode) app.MapEquityLensE2E();

if (await ApplicationCliDispatcher.TryExecuteAsync(app, args)) return;

if (e2eTestMode) await app.SeedEquityLensE2EAsync();

app.Run();
