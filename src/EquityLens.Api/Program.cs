using System.Text;
using EquityLens.Api.Bootstrap;
using EquityLens.Api.Services.Agents;
using EquityLens.Api.Services.Auth;

Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

var builder = WebApplication.CreateBuilder(args);
builder.Host.UseDefaultServiceProvider(o => o.ValidateOnBuild = false);
var e2eTestMode = E2ETestAccess.IsEnabled(builder.Configuration, builder.Environment);

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

app.UseForwardedHeaders(); // 必須最先執行：讓後面的限流與日誌拿到 Caddy 轉發的真實 IP
app.UseExceptionHandler();
app.UseCors("AllowFrontend");
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter(); // 放在授權之後：未登入的請求先被 401 擋下，限流才能依使用者分區
app.MapGet("/healthz", () => Results.Ok(new { status = "ok" }));
app.MapControllers();
if (e2eTestMode) app.MapEquityLensE2E();

if (await ApplicationCliDispatcher.TryExecuteAsync(app, args)) return;

if (e2eTestMode) await app.SeedEquityLensE2EAsync();

app.Run();
