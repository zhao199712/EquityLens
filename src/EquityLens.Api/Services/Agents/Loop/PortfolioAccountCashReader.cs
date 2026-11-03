using EquityLens.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace EquityLens.Api.Services.Agents.Loop;

/// <summary>唯讀擷取已授權投組的帳簿現金，不重建入金、不推測券商餘額或換匯。</summary>
internal static class PortfolioAccountCashReader
{
    internal sealed record Evidence(string Status, decimal? Balance, string Currency, DateOnly AsOf,
        string Basis, bool IncludesSystemDerivedFunding, string? Reason, string Warning);

    // 呼叫端必須先以 run.UserId 驗證 portfolio 的所有權；不接受模型傳入 portfolioId。
    internal static async Task<Evidence> ReadAsync(EquityLensDbContext db, Guid portfolioId,
        string currency, DateOnly requestedDate, CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var asOf = requestedDate < today ? requestedDate : today;
        var flows = await db.PortfolioCashFlows.AsNoTracking()
            .Where(x => x.PortfolioId == portfolioId && x.EffectiveDate <= asOf
                && x.Status != "Skipped" && x.Status != "Scheduled").ToListAsync(ct);
        var transactions = await db.PortfolioTransactions.AsNoTracking()
            .Where(x => x.PortfolioId == portfolioId && x.TransactionDate <= asOf)
            .Select(x => new { x.TransactionType, x.Quantity, x.Price, x.Fee, Currency = x.Security.Currency })
            .ToListAsync(ct);
        var derived = flows.Any(x => x.IsSystemDerived);
        const string warning = "僅依系統記錄的入出金、股息、交易及費用計算，非券商核對餘額；系統推導入金不代表真實轉帳。持股權重不含此現金，情境假設也不可代替帳戶餘額。";
        Evidence Unknown(string reason) => new("Unavailable", null, currency, asOf, "RecordedLedger", derived, reason, warning);
        if (flows.Count == 0) return Unknown("MissingCashLedger");
        if (flows.Any(x => x.Status is not ("Posted" or "Applied" or "Confirmed")))
            return Unknown("UnconfirmedCashFlow");
        if (flows.Any(x => !string.Equals(x.Currency, currency, StringComparison.OrdinalIgnoreCase))
            || transactions.Any(x => !string.Equals(x.Currency, currency, StringComparison.OrdinalIgnoreCase)))
            return Unknown("CurrencyConversionRequired");
        if (flows.Any(x => x.FlowType is not ("Deposit" or "Withdrawal" or "Dividend" or "DividendTax" or "Fee"))
            || transactions.Any(x => x.TransactionType is not ("BUY" or "SELL")))
            return Unknown("UnsupportedLedgerEntry");
        var balance = flows.Sum(x => x.FlowType is "Withdrawal" or "DividendTax" or "Fee" ? -x.Amount : x.Amount)
            + transactions.Sum(x => x.TransactionType == "BUY" ? -(x.Quantity * x.Price + x.Fee) : x.Quantity * x.Price - x.Fee);
        return new("Available", balance, currency, asOf, "RecordedLedger", derived, null, warning);
    }
}
