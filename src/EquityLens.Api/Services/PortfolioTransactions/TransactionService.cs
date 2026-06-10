using EquityLens.Api.Common;
using EquityLens.Api.Contracts.PortfolioTransactions;
using EquityLens.Api.Data;
using EquityLens.Api.Data.Entities;
using EquityLens.Api.Repositories.PortfolioHoldings;
using EquityLens.Api.Repositories.Portfolios;
using EquityLens.Api.Repositories.PortfolioTransactions;
using EquityLens.Api.Repositories.Securities;
using EquityLens.Api.Services.CurrentUser;

namespace EquityLens.Api.Services.PortfolioTransactions;

public sealed class TransactionService : ITransactionService
{
    private readonly EquityLensDbContext _dbContext;
    private readonly ICurrentUserContext _currentUser;
    private readonly IPortfolioRepository _portfolioRepository;
    private readonly ITransactionRepository _transactionRepository;
    private readonly IPortfolioHoldingRepository _holdingRepository;
    private readonly ISecurityRepository _securityRepository;

    public TransactionService(
        EquityLensDbContext dbContext,
        ICurrentUserContext currentUser,
        IPortfolioRepository portfolioRepository,
        ITransactionRepository transactionRepository,
        IPortfolioHoldingRepository holdingRepository,
        ISecurityRepository securityRepository)
    {
        _dbContext = dbContext;
        _currentUser = currentUser;
        _portfolioRepository = portfolioRepository;
        _transactionRepository = transactionRepository;
        _holdingRepository = holdingRepository;
        _securityRepository = securityRepository;
    }

    public async Task<Result<IReadOnlyList<TransactionResponse>>> ListAsync(
        Guid portfolioId, Guid? securityId, CancellationToken cancellationToken)
    {
        if (!await _portfolioRepository.ActiveExistsAsync(portfolioId, _currentUser.UserId, cancellationToken))
            return Result<IReadOnlyList<TransactionResponse>>.Failure("portfolio.not_found", "Portfolio was not found.");

        var transactions = await _transactionRepository.ListAsync(portfolioId, securityId, cancellationToken);

        var responses = new List<TransactionResponse>();
        foreach (var tx in transactions)
        {
            var sec = await _securityRepository.GetEntityAsync(tx.SecurityId, cancellationToken);
            if (sec is null) continue;
            responses.Add(new TransactionResponse(
                tx.Id, tx.SecurityId, sec.Ticker, sec.Exchange, sec.Name,
                tx.TransactionType, tx.Quantity, tx.Price, tx.Fee,
                tx.TransactionDate, tx.Note, tx.CreatedAtUtc));
        }

        return Result<IReadOnlyList<TransactionResponse>>.Success(responses);
    }

    public async Task<Result<TransactionResponse>> CreateAsync(
        Guid portfolioId, CreateTransactionRequest request, CancellationToken cancellationToken)
    {
        if (!await _portfolioRepository.ActiveExistsAsync(portfolioId, _currentUser.UserId, cancellationToken))
            return Result<TransactionResponse>.Failure("portfolio.not_found", "Portfolio was not found.");

        var txType = request.TransactionType.ToUpperInvariant();
        if (txType is not ("BUY" or "SELL"))
            return Result<TransactionResponse>.Failure("transaction.invalid_type", "Transaction type must be BUY or SELL.");

        if (request.Quantity <= 0)
            return Result<TransactionResponse>.Failure("transaction.invalid_quantity", "Quantity must be greater than zero.");

        if (request.Price < 0)
            return Result<TransactionResponse>.Failure("transaction.invalid_price", "Price cannot be negative.");

        var security = await _securityRepository.GetEntityAsync(request.SecurityId, cancellationToken);
        if (security is null)
            return Result<TransactionResponse>.Failure("security.not_found", "Security was not found.");

        if (txType == "SELL")
        {
            var holding = await _holdingRepository.GetBySecurityIdAsync(portfolioId, request.SecurityId, cancellationToken);
            if (holding is null || holding.Quantity < request.Quantity)
                return Result<TransactionResponse>.Failure("transaction.insufficient_quantity", "Insufficient quantity to sell.");
        }

        var transaction = new PortfolioTransaction
        {
            PortfolioId = portfolioId,
            SecurityId = request.SecurityId,
            TransactionType = txType,
            Quantity = request.Quantity,
            Price = request.Price,
            Fee = request.Fee ?? 0m,
            TransactionDate = request.TransactionDate,
            Note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim(),
            CreatedAtUtc = DateTime.UtcNow
        };

        _transactionRepository.Add(transaction);
        await _dbContext.SaveChangesAsync(cancellationToken);
        await RecalculateHoldingAsync(portfolioId, request.SecurityId, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);

        var response = new TransactionResponse(
            transaction.Id, transaction.SecurityId, security.Ticker, security.Exchange, security.Name,
            transaction.TransactionType, transaction.Quantity, transaction.Price, transaction.Fee,
            transaction.TransactionDate, transaction.Note, transaction.CreatedAtUtc);

        return Result<TransactionResponse>.Success(response);
    }

    public async Task<Result<bool>> DeleteAsync(
        Guid portfolioId, Guid transactionId, CancellationToken cancellationToken)
    {
        if (!await _portfolioRepository.ActiveExistsAsync(portfolioId, _currentUser.UserId, cancellationToken))
            return Result<bool>.Failure("portfolio.not_found", "Portfolio was not found.");

        var transaction = await _transactionRepository.GetAsync(portfolioId, transactionId, cancellationToken);
        if (transaction is null)
            return Result<bool>.Failure("transaction.not_found", "Transaction was not found.");

        var securityId = transaction.SecurityId;
        _transactionRepository.Remove(transaction);
        await _dbContext.SaveChangesAsync(cancellationToken);
        await RecalculateHoldingAsync(portfolioId, securityId, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return Result<bool>.Success(true);
    }

    public async Task<Result<bool>> DeleteAllBySecurityAsync(
        Guid portfolioId, Guid securityId, CancellationToken cancellationToken)
    {
        if (!await _portfolioRepository.ActiveExistsAsync(portfolioId, _currentUser.UserId, cancellationToken))
            return Result<bool>.Failure("portfolio.not_found", "Portfolio was not found.");

        var transactions = await _transactionRepository.ListByHoldingAsync(portfolioId, securityId, cancellationToken);
        foreach (var tx in transactions)
            _transactionRepository.Remove(tx);

        var holding = await _holdingRepository.GetBySecurityIdAsync(portfolioId, securityId, cancellationToken);
        if (holding is not null)
            _holdingRepository.Remove(holding);

        await _dbContext.SaveChangesAsync(cancellationToken);
        return Result<bool>.Success(true);
    }

    private async Task RecalculateHoldingAsync(
        Guid portfolioId, Guid securityId, CancellationToken cancellationToken)
    {
        var transactions = await _transactionRepository.ListByHoldingAsync(portfolioId, securityId, cancellationToken);

        decimal totalBuyQty = 0;
        decimal totalBuyCost = 0m;
        decimal totalSellQty = 0;

        foreach (var tx in transactions)
        {
            var cost = tx.Quantity * tx.Price + tx.Fee;
            if (tx.TransactionType == "BUY")
            {
                totalBuyQty += tx.Quantity;
                totalBuyCost += cost;
            }
            else
            {
                totalSellQty += tx.Quantity;
            }
        }

        var netQuantity = totalBuyQty - totalSellQty;
        var holding = await _holdingRepository.GetBySecurityIdAsync(portfolioId, securityId, cancellationToken);

        if (netQuantity <= 0)
        {
            if (holding is not null)
                _holdingRepository.Remove(holding);
            return;
        }

        var avgCost = totalBuyQty == 0 ? 0 : totalBuyCost / totalBuyQty;

        if (holding is null)
        {
            var security = await _securityRepository.GetEntityAsync(securityId, cancellationToken);
            holding = new PortfolioHolding
            {
                PortfolioId = portfolioId,
                SecurityId = securityId,
                Quantity = netQuantity,
                AverageCost = avgCost,
                CostCurrency = security?.Currency ?? "TWD",
                UpdatedAtUtc = DateTime.UtcNow
            };
            _holdingRepository.Add(holding);
        }
        else
        {
            holding.Quantity = netQuantity;
            holding.AverageCost = avgCost;
            holding.UpdatedAtUtc = DateTime.UtcNow;
        }
    }
}
