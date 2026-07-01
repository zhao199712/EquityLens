using Microsoft.EntityFrameworkCore;
using Pgvector.EntityFrameworkCore;
using EquityLens.Api.Data.Entities;

namespace EquityLens.Api.Data;

public class EquityLensDbContext : DbContext
{
    public EquityLensDbContext(DbContextOptions<EquityLensDbContext> options) : base(options)
    {
    }

    // Core
    public DbSet<AppUser> Users => Set<AppUser>();
    public DbSet<Portfolio> Portfolios => Set<Portfolio>();
    public DbSet<Security> Securities => Set<Security>();
    public DbSet<PortfolioHolding> PortfolioHoldings => Set<PortfolioHolding>();
    public DbSet<PortfolioSnapshot> PortfolioSnapshots => Set<PortfolioSnapshot>();

    // Market Data
    public DbSet<MarketPrice> MarketPrices => Set<MarketPrice>();
    public DbSet<FinancialStatement> FinancialStatements => Set<FinancialStatement>();
    public DbSet<FinancialLineItem> FinancialLineItems => Set<FinancialLineItem>();

    // Risk
    public DbSet<RiskModelSetting> RiskModelSettings => Set<RiskModelSetting>();
    public DbSet<RiskRun> RiskRuns => Set<RiskRun>();
    public DbSet<RiskMetric> RiskMetrics => Set<RiskMetric>();
    public DbSet<Scenario> Scenarios => Set<Scenario>();
    public DbSet<ScenarioResult> ScenarioResults => Set<ScenarioResult>();

    // Reports & AI
    public DbSet<FinancialReport> FinancialReports => Set<FinancialReport>();
    public DbSet<AiMemo> AiMemos => Set<AiMemo>();
    public DbSet<Citation> Citations => Set<Citation>();
    public DbSet<CriticNote> CriticNotes => Set<CriticNote>();

    // Documents
    public DbSet<UploadedFile> UploadedFiles => Set<UploadedFile>();
    public DbSet<Document> Documents => Set<Document>();
    public DbSet<DocumentChunk> DocumentChunks => Set<DocumentChunk>();
    public DbSet<DocumentEmbedding> DocumentEmbeddings => Set<DocumentEmbedding>();
    public DbSet<FinancialFiling> FinancialFilings => Set<FinancialFiling>();
    public DbSet<InvestorConference> InvestorConferences => Set<InvestorConference>();

    // Jobs
    public DbSet<JobRun> JobRuns => Set<JobRun>();

    // Chat
    public DbSet<ChatSession> ChatSessions => Set<ChatSession>();
    public DbSet<ChatMessage> ChatMessages => Set<ChatMessage>();

    // Research trace
    public DbSet<ResearchRun> ResearchRuns => Set<ResearchRun>();
    public DbSet<ResearchRunStep> ResearchRunSteps => Set<ResearchRunStep>();
    public DbSet<ResearchRunCandidate> ResearchRunCandidates => Set<ResearchRunCandidate>();
    public DbSet<ResearchRunCitation> ResearchRunCitations => Set<ResearchRunCitation>();

    // Agent workflows
    public DbSet<AgentRun> AgentRuns => Set<AgentRun>();
    public DbSet<AgentRunNode> AgentRunNodes => Set<AgentRunNode>();
    public DbSet<AgentRunEvent> AgentRunEvents => Set<AgentRunEvent>();
    public DbSet<AgentToolCall> AgentToolCalls => Set<AgentToolCall>();
    public DbSet<AgentFeedback> AgentFeedback => Set<AgentFeedback>();

    // Exchange Rates
    public DbSet<ExchangeRate> ExchangeRates => Set<ExchangeRate>();

    // Transactions
    public DbSet<PortfolioTransaction> PortfolioTransactions => Set<PortfolioTransaction>();

    // Auth
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(EquityLensDbContext).Assembly);
    }
}
