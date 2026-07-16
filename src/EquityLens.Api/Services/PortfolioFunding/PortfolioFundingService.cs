using EquityLens.Api.Data;
using EquityLens.Api.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace EquityLens.Api.Services.PortfolioFunding;

/// <summary>
/// 將既有交易轉成最低必要的外部投入。買賣與股息都是帳內活動，只有買入當日現金不足才建立隱含入金。
/// </summary>
public sealed class PortfolioFundingService : IPortfolioFundingService
{
    private const string ImplicitFundingNote = "系統推導：買入資金";
    private readonly EquityLensDbContext _db;
    public PortfolioFundingService(EquityLensDbContext db) => _db = db;

    public async Task RebuildImplicitFundingAsync(Guid portfolioId, CancellationToken cancellationToken)
    {
        var existingImplicit = await _db.PortfolioCashFlows
            .Where(x => x.PortfolioId == portfolioId && (x.Note == ImplicitFundingNote || x.Note == "系統建立：既有交易初始入金"))
            .ToListAsync(cancellationToken);
        _db.PortfolioCashFlows.RemoveRange(existingImplicit);

        var transactions = await _db.PortfolioTransactions.Where(x => x.PortfolioId == portfolioId)
            .OrderBy(x => x.TransactionDate).ThenBy(x => x.CreatedAtUtc).ThenBy(x => x.Id).ToListAsync(cancellationToken);
        var cashFlows = await _db.PortfolioCashFlows.Where(x => x.PortfolioId == portfolioId && x.Status != "Skipped" && x.Note != ImplicitFundingNote && x.Note != "系統建立：既有交易初始入金")
            .OrderBy(x => x.EffectiveDate).ThenBy(x => x.CreatedAtUtc).ToListAsync(cancellationToken);

        decimal cash = 0m;
        var dates = transactions.Select(x => x.TransactionDate).Concat(cashFlows.Select(x => x.EffectiveDate)).Distinct().OrderBy(x => x);
        foreach (var date in dates)
        {
            foreach (var flow in cashFlows.Where(x => x.EffectiveDate == date))
                cash += SignedAmount(flow);

            foreach (var transaction in transactions.Where(x => x.TransactionDate == date)
                .OrderBy(x => x.TransactionType == "SELL" ? 0 : 1)
                .ThenBy(x => x.CreatedAtUtc)
                .ThenBy(x => x.Id))
            {
                if (transaction.TransactionType == "SELL")
                {
                    cash += transaction.Quantity * transaction.Price - transaction.Fee;
                    continue;
                }

                var cost = transaction.Quantity * transaction.Price + transaction.Fee;
                if (cash < cost)
                {
                    var required = cost - cash;
                    _db.PortfolioCashFlows.Add(new PortfolioCashFlow
                    {
                        PortfolioId = portfolioId,
                        FlowType = "Deposit",
                        Amount = required,
                        Currency = "TWD",
                        EffectiveDate = date,
                        Status = "Posted",
                        IsUserAdjusted = false,
                        IsSystemDerived = true,
                        Note = ImplicitFundingNote
                    });
                    cash += required;
                }
                cash -= cost;
            }
        }
    }

    private static decimal SignedAmount(PortfolioCashFlow flow)
        => flow.FlowType is "Withdrawal" or "Fee" or "DividendTax" ? -flow.Amount : flow.Amount;
}
