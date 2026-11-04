using System.Text.Json;
using System.Text.Json.Nodes;
using EquityLens.Api.Contracts.Agents;
using EquityLens.Api.Contracts.Chat;
using EquityLens.Api.Contracts.Research;
using EquityLens.Api.Controllers;
using EquityLens.Api.Data;
using EquityLens.Api.Data.Entities;
using EquityLens.Api.Services.Agents;
using EquityLens.Api.Services.Ai;
using EquityLens.Api.Services.Chat;
using EquityLens.Api.Services.CurrentUser;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging.Abstractions;

// Capture only. Expected answers are never inspected; scoring is a separate process.
if (args.Length < 2) throw new ArgumentException("cases.jsonl output-directory [--smoke]");
var smoke = args.Contains("--smoke");
var output = Path.GetFullPath(args[1]);
Directory.CreateDirectory(output);
var predictionsPath = Path.Combine(output, "predictions.jsonl");
if (File.Exists(predictionsPath)) throw new InvalidOperationException("Refuse to overwrite predictions.");
var json = new JsonSerializerOptions(JsonSerializerDefaults.Web);
IChatCompletionService provider = smoke ? new SmokeChat() : new DeepSeekChatCompletionService(new HttpClient(), Options.Create(new DeepSeekOptions {
    BaseUrl = "http://127.0.0.1:5098", ApiKey = Environment.GetEnvironmentVariable("DEEPSEEK_API_KEY") ?? throw new InvalidOperationException("Missing API key"),
    Model = "deepseek-flash", ThinkingEnabled = false
}));
foreach (var line in File.ReadLines(args[0]).Where(l => !string.IsNullOrWhiteSpace(l)))
{
    var item = JsonNode.Parse(line)!;
    var id = item["id"]!.GetValue<string>();
    if (smoke && id is not ("router-01" or "router-25" or "router-26")) continue;
    var message = item["message"]!.GetValue<string>();
    var fixture = item["context"]!;
    await using var db = new FixtureDb(new DbContextOptionsBuilder<EquityLensDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    var user = Guid.NewGuid();
    var securities = new Dictionary<string, string> { ["2330"]="台積電", ["2317"]="鴻海", ["2327"]="國巨", ["2454"]="聯發科", ["2308"]="台達電", ["0050"]="元大台灣50", ["006208"]="富邦台50" }
        .ToDictionary(p => p.Key, p => new Security { Id=Guid.NewGuid(), Ticker=p.Key, Name=p.Value, IsActive=true });
    db.Securities.AddRange(securities.Values);
    foreach (var p in fixture["portfolios"]!.AsArray())
    {
        var portfolio = new Portfolio { Id=Guid.Parse(p!["id"]!.GetValue<string>()), OwnerUserId=user, Name=p["name"]!.GetValue<string>(), BaseCurrency="TWD", IsActive=true };
        foreach (var ticker in p["holdings"]!.AsArray().Select(t=>t!.GetValue<string>()))
            portfolio.Holdings.Add(new PortfolioHolding { Id=Guid.NewGuid(), PortfolioId=portfolio.Id, SecurityId=securities[ticker].Id, Security=securities[ticker], Quantity=100, AverageCost=100, CostCurrency="TWD" });
        db.Portfolios.Add(portfolio);
    }
    var context = new JsonObject();
    if (fixture["prior_run"] is { } prior)
    {
        if (prior["workflow"]!.GetValue<string>() != "Research") throw new NotSupportedException("Unsupported fixture prior workflow");
        var owner = prior["owned"]!.GetValue<bool>() ? user : Guid.NewGuid();
        var research = new ResearchRun { Id=Guid.NewGuid(), UserId=owner, Ticker=prior["ticker"]!.GetValue<string>(), Question="上一份研究", Status="Answered" };
        var parent = new AgentRun { Id=Guid.NewGuid(), UserId=owner, ResearchRunId=research.Id, WorkflowType=AgentWorkflowTypes.ResearchAgent, Status=AgentRunStatuses.Succeeded };
        db.ResearchRuns.Add(research); db.AgentRuns.Add(parent);
        context["lastAgentRunId"] = parent.Id.ToString();
    }
    var session = new ChatSession { Id=Guid.NewGuid(), UserId=user, ContextJson=context.ToJsonString() };
    db.ChatSessions.Add(session);
    await db.SaveChangesAsync();
    var chat = new RecordingChat(provider);
    var router = new InvestmentResearchRouter(chat);
    var sink = new CaptureRuns();
    var queries = new AgentWorkflowQueryService(db, router, sink);
    var conversation = new ConversationService(db, router, queries, sink, new AgentWorkflowCatalog());
    var entry = fixture["entry"]!.GetValue<string>();
    string action; object? detail;
    try
    {
        if (entry.StartsWith("research:", StringComparison.Ordinal))
        {
            // Invoke the actual explicit-entry controller; unrelated dependencies are deliberately absent.
            var controller = new ResearchController(null!, null!, null!, null!, sink, new FixtureUser(user), NullLogger<ResearchController>.Instance);
            var result = await controller.CreateInvestigation(new ResearchAskRequest(entry.Split(':')[1], message), CancellationToken.None);
            action = sink.ResearchRequest is null ? "clarify" : "route";
            detail = new { path="ResearchController.CreateInvestigation", httpResult=result.Result?.GetType().Name };
        }
        else
        {
            var outcome = await conversation.ProcessAsync(user, session.Id, new SendMessageRequest(message, Guid.NewGuid()));
            action = outcome.Action switch { ConversationActions.RouteWorkflow => "route", ConversationActions.RejectUnsafe => "reject", ConversationActions.DirectResponse => "direct_reply", _ => "clarify" };
            detail = outcome;
        }
    }
    catch (Exception ex) { action="error"; detail=new { exception=ex.GetType().Name, message=ex.Message }; }
    string? workflow = null;
    var parameters = new JsonObject();
    if (action == "route" && sink.ResearchRequest is { } request) { workflow="Research"; parameters["ticker"]=request.Ticker; }
    if (action == "route" && sink.PortfolioId is { } portfolioId) {
        workflow="Portfolio"; parameters["portfolioId"]=portfolioId.ToString();
        if (sink.RoutingContext?.Scenarios is { Count: > 0 } scenarios) parameters["scenarios"]=JsonSerializer.SerializeToNode(scenarios,json);
    }
    var prediction = new { id, action, workflow, @params=parameters };
    await File.AppendAllTextAsync(predictionsPath, JsonSerializer.Serialize(prediction,json)+"\n");
    await File.WriteAllTextAsync(Path.Combine(output,id+".json"),JsonSerializer.Serialize(new { id, message, fixture, entry, calls=chat.Calls, detail, prediction },new JsonSerializerOptions(json) { WriteIndented=true }));
    Console.WriteLine($"{id}: {action} {workflow ?? "-"}; model calls={chat.Calls.Count}");
}

sealed class FixtureDb(DbContextOptions<EquityLensDbContext> options) : EquityLensDbContext(options) {
    protected override void OnModelCreating(ModelBuilder b) { base.OnModelCreating(b); b.Ignore<DocumentEmbedding>(); }
}
sealed class FixtureUser(Guid id) : ICurrentUserContext { public Guid UserId=>id; public string Email=>"router-fixture@example.invalid"; public string DisplayName=>"Router fixture"; public bool IsAuthenticated=>true; }
sealed class RecordingChat(IChatCompletionService inner) : IChatCompletionService {
    public string Provider=>inner.Provider; public string Model=>inner.Model;
    public List<object> Calls { get; }=[];
    public async Task<ChatCompletionResult> CompleteAsync(ChatCompletionRequest request,CancellationToken ct=default) {
        try { var response=await inner.CompleteAsync(request,ct); Calls.Add(new { request,response }); return response; }
        catch(Exception ex) { Calls.Add(new { request,error=ex.GetType().Name }); throw; }
    }
}
sealed class SmokeChat : IChatCompletionService {
    public string Provider=>"offline"; public string Model=>"smoke";
    public Task<ChatCompletionResult> CompleteAsync(ChatCompletionRequest request,CancellationToken ct=default)=>Task.FromResult(new ChatCompletionResult("{\"action\":\"route\",\"workflow\":\"Research\",\"params\":{\"ticker\":\"2330\"},\"missing\":[],\"confidence\":\"high\"}", Model,0,0));
}
sealed class CaptureRuns : IAgentRunService
    {
        public Guid? PortfolioId { get; private set; }
        public DateOnly? From { get; private set; }
        public DateOnly? To { get; private set; }
        public ResearchAskRequest? ResearchRequest { get; private set; }
        public InvestmentResearchRoutingContext? RoutingContext { get; private set; }
        public Task<AgentRunSummaryResponse> CreatePortfolioDiagnosisAsync(Guid userId, Guid portfolioId, DateOnly? from, DateOnly? to, InvestmentResearchRoutingContext? routingContext = null, CancellationToken cancellationToken = default)
        { PortfolioId = portfolioId; From = from; To = to; RoutingContext = routingContext; return Task.FromResult(Summary(AgentWorkflowTypes.PortfolioDiagnosis)); }
        public Task<(AgentRunSummaryResponse AgentRun, Guid ResearchRunId)> CreateResearchInvestigationAsync(Guid userId, ResearchAskRequest request, InvestmentResearchRoutingContext? routingContext = null, CancellationToken cancellationToken = default)
        { ResearchRequest = request; RoutingContext = routingContext; return Task.FromResult((Summary(AgentWorkflowTypes.ResearchAgent), Guid.NewGuid())); }
        private static AgentRunSummaryResponse Summary(string workflow) => new(Guid.NewGuid(), workflow, "Agent", AgentRunStatuses.Pending, DateTime.UtcNow, null, null, null, 0, 0, 0);
        public Task<AgentRunSummaryResponse> CreateCriticReviewAsync(Guid userId, Guid researchRunId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<SubmitAgentFeedbackResponse> SubmitFeedbackAsync(Guid runId, Guid userId, SubmitAgentFeedbackRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<AgentRunSummaryResponse>> ListChildrenAsync(Guid parentRunId, Guid userId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<AgentRunSummaryResponse>> ListAsync(Guid? userId, int limit = 50, string? workflowType = null, string? status = null, Guid? researchRunId = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<AgentRunDetailResponse?> GetByIdAsync(Guid id, Guid? userId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<AgentRunSummaryResponse?> RetryAsync(Guid id, Guid userId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<AgentRunSummaryResponse?> CancelAsync(Guid id, Guid userId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<AgentRunSummaryResponse> DecideApprovalAsync(Guid runId, Guid userId, string decision, string? comment, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
