using EquityLens.Api.Data.Entities;

namespace EquityLens.Api.Services.PortfolioTransactions;

/// <summary>由不可變交易紀錄重播的 FIFO 成本計算器。</summary>
public static class FifoPortfolioCalculator
{
    public static FifoPortfolioResult Calculate(IEnumerable<PortfolioTransaction> source)
    {
        var lotsBySecurity = new Dictionary<Guid, Queue<FifoLot>>();
        var sales = new Dictionary<Guid, FifoSale>();

        foreach (var tx in source.OrderBy(x => x.TransactionDate).ThenBy(x => x.CreatedAtUtc).ThenBy(x => x.Id))
        {
            if (!lotsBySecurity.TryGetValue(tx.SecurityId, out var lots))
                lotsBySecurity[tx.SecurityId] = lots = new Queue<FifoLot>();

            if (tx.TransactionType == "BUY")
            {
                lots.Enqueue(new FifoLot(tx.Id, tx.Quantity, tx.Quantity * tx.Price + tx.Fee));
                continue;
            }

            var available = lots.Sum(x => x.Quantity);
            if (tx.TransactionType != "SELL" || available < tx.Quantity)
                return FifoPortfolioResult.Invalid($"交易 {tx.Id} 的賣出數量超過當時可持有數量。");

            var quantityToMatch = tx.Quantity;
            decimal matchedCost = 0m;
            while (quantityToMatch > 0)
            {
                var lot = lots.Dequeue();
                var used = Math.Min(quantityToMatch, lot.Quantity);
                var cost = lot.CostValue * used / lot.Quantity;
                matchedCost += cost;
                quantityToMatch -= used;
                if (used < lot.Quantity)
                {
                    // 剩餘部位仍是最早批次，必須放回佇列最前端。
                    var remaining = new Queue<FifoLot>();
                    remaining.Enqueue(lot with { Quantity = lot.Quantity - used, CostValue = lot.CostValue - cost });
                    foreach (var laterLot in lots) remaining.Enqueue(laterLot);
                    lots.Clear();
                    foreach (var queuedLot in remaining) lots.Enqueue(queuedLot);
                }
            }

            var proceeds = tx.Quantity * tx.Price - tx.Fee;
            sales[tx.Id] = new FifoSale(proceeds, matchedCost, proceeds - matchedCost);
        }

        return FifoPortfolioResult.Valid(lotsBySecurity, sales);
    }
}

public sealed record FifoLot(Guid BuyTransactionId, decimal Quantity, decimal CostValue);
public sealed record FifoSale(decimal NetProceeds, decimal MatchedCost, decimal RealizedPnl);

public sealed class FifoPortfolioResult
{
    private FifoPortfolioResult(bool isValid, string? error, IReadOnlyDictionary<Guid, Queue<FifoLot>> lotsBySecurity, IReadOnlyDictionary<Guid, FifoSale> sales)
    { IsValid = isValid; Error = error; LotsBySecurity = lotsBySecurity; Sales = sales; }
    public bool IsValid { get; }
    public string? Error { get; }
    public IReadOnlyDictionary<Guid, Queue<FifoLot>> LotsBySecurity { get; }
    public IReadOnlyDictionary<Guid, FifoSale> Sales { get; }
    public decimal TotalRealizedPnl => Sales.Values.Sum(x => x.RealizedPnl);
    public static FifoPortfolioResult Valid(Dictionary<Guid, Queue<FifoLot>> lots, Dictionary<Guid, FifoSale> sales) => new(true, null, lots, sales);
    public static FifoPortfolioResult Invalid(string error) => new(false, error, new Dictionary<Guid, Queue<FifoLot>>(), new Dictionary<Guid, FifoSale>());
}
