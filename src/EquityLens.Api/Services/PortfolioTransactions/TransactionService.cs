using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using EquityLens.Api.Common;
using EquityLens.Api.Contracts.PortfolioTransactions;
using EquityLens.Api.Data;
using EquityLens.Api.Data.Entities;
using EquityLens.Api.Repositories.PortfolioHoldings;
using EquityLens.Api.Repositories.Portfolios;
using EquityLens.Api.Repositories.PortfolioTransactions;
using EquityLens.Api.Repositories.Securities;
using EquityLens.Api.Services.CurrentUser;
using EquityLens.Api.Services.PortfolioFunding;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace EquityLens.Api.Services.PortfolioTransactions;

public sealed class TransactionService : ITransactionService
{
    private const string AutomaticDividendNote = "FinMind 現金股利";

    private readonly EquityLensDbContext _dbContext;
    private readonly ICurrentUserContext _currentUser;
    private readonly IPortfolioRepository _portfolioRepository;
    private readonly ITransactionRepository _transactionRepository;
    private readonly IPortfolioHoldingRepository _holdingRepository;
    private readonly ISecurityRepository _securityRepository;
    private readonly IPortfolioFundingService _portfolioFundingService;

    public TransactionService(
        EquityLensDbContext dbContext,
        ICurrentUserContext currentUser,
        IPortfolioRepository portfolioRepository,
        ITransactionRepository transactionRepository,
        IPortfolioHoldingRepository holdingRepository,
        ISecurityRepository securityRepository,
        IPortfolioFundingService portfolioFundingService)
    {
        _dbContext = dbContext;
        _currentUser = currentUser;
        _portfolioRepository = portfolioRepository;
        _transactionRepository = transactionRepository;
        _holdingRepository = holdingRepository;
        _securityRepository = securityRepository;
        _portfolioFundingService = portfolioFundingService;
    }

    public async Task<Result<IReadOnlyList<TransactionResponse>>> ListAsync(
        Guid portfolioId, Guid? securityId, CancellationToken cancellationToken)
    {
        if (!await _portfolioRepository.ActiveExistsAsync(portfolioId, _currentUser.UserId, cancellationToken))
            return Result<IReadOnlyList<TransactionResponse>>.Failure("portfolio.not_found", "Portfolio was not found.");

        var transactions = await _transactionRepository.ListAsync(portfolioId, securityId, cancellationToken);

        var fifo = FifoPortfolioCalculator.Calculate(transactions);
        var responses = new List<TransactionResponse>();
        foreach (var tx in transactions)
        {
            var sec = await _securityRepository.GetEntityAsync(tx.SecurityId, cancellationToken);
            if (sec is null) continue;
            responses.Add(ToResponse(
                tx,
                sec,
                fifo.Sales.TryGetValue(tx.Id, out var sale) ? sale : null));
        }

        return Result<IReadOnlyList<TransactionResponse>>.Success(responses);
    }

    public async Task<Result<TransactionCreateResult>> CreateAsync(
        Guid portfolioId,
        CreateTransactionRequest request,
        string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        if (!TryNormalizeIdempotencyKey(idempotencyKey, out var normalizedKey, out var keyError))
            return Result<TransactionCreateResult>.Failure(keyError!.Code, keyError.Message);

        if (!TryNormalizeRequest(request, out var normalizedRequest, out var requestError))
            return Result<TransactionCreateResult>.Failure(requestError!.Code, requestError.Message);

        var requestHash = ComputeRequestHash(normalizedRequest);
        var security = await _securityRepository.GetEntityAsync(normalizedRequest.SecurityId, cancellationToken);
        if (security is null)
            return Result<TransactionCreateResult>.Failure("security.not_found", "Security was not found.");

        await using var dbTransaction = await BeginMutationTransactionAsync(cancellationToken);
        try
        {
            var portfolio = await LockActivePortfolioAsync(portfolioId, cancellationToken);
            if (portfolio is null)
            {
                await RollbackAsync(dbTransaction);
                return Result<TransactionCreateResult>.Failure("portfolio.not_found", "Portfolio was not found.");
            }

            var existingByKey = await _dbContext.PortfolioTransactions
                .SingleOrDefaultAsync(
                    x => x.PortfolioId == portfolio.Id && x.IdempotencyKey == normalizedKey,
                    cancellationToken);
            if (existingByKey is not null)
            {
                if (!string.Equals(existingByKey.RequestHash, requestHash, StringComparison.Ordinal))
                {
                    await RollbackAsync(dbTransaction);
                    return Result<TransactionCreateResult>.Failure(
                        "transaction.idempotency_conflict",
                        "The Idempotency-Key was already used with a different request payload.");
                }

                var replayResponse = await BuildResponseAsync(existingByKey, security, cancellationToken);
                await CommitAsync(dbTransaction, cancellationToken);
                return Result<TransactionCreateResult>.Success(new TransactionCreateResult(replayResponse, true));
            }

            var existing = await _dbContext.PortfolioTransactions
                .AsNoTracking()
                .Where(x => x.PortfolioId == portfolio.Id)
                .ToListAsync(cancellationToken);

            var transaction = new PortfolioTransaction
            {
                Id = Guid.NewGuid(),
                PortfolioId = portfolio.Id,
                SecurityId = normalizedRequest.SecurityId,
                TransactionType = normalizedRequest.TransactionType,
                Quantity = normalizedRequest.Quantity,
                Price = normalizedRequest.Price,
                Fee = normalizedRequest.Fee,
                TransactionDate = normalizedRequest.TransactionDate,
                Note = normalizedRequest.Note,
                IdempotencyKey = normalizedKey,
                RequestHash = requestHash,
                CreatedAtUtc = DateTime.UtcNow
            };

            var projected = FifoPortfolioCalculator.Calculate(existing.Append(transaction));
            if (!projected.IsValid)
            {
                await RollbackAsync(dbTransaction);
                return Result<TransactionCreateResult>.Failure(
                    "transaction.insufficient_quantity",
                    "Insufficient quantity to sell.");
            }

            _transactionRepository.Add(transaction);
            await _dbContext.SaveChangesAsync(cancellationToken);

            await RecalculateHoldingAsync(portfolio.Id, transaction.SecurityId, cancellationToken);
            await ReconcileDividendCashFlowsAsync(portfolio.Id, transaction.SecurityId, cancellationToken);
            await _dbContext.SaveChangesAsync(cancellationToken);

            await _portfolioFundingService.RebuildImplicitFundingAsync(portfolio.Id, cancellationToken);
            await _dbContext.SaveChangesAsync(cancellationToken);

            var response = ToResponse(
                transaction,
                security,
                projected.Sales.TryGetValue(transaction.Id, out var sale) ? sale : null);
            await CommitAsync(dbTransaction, cancellationToken);
            return Result<TransactionCreateResult>.Success(new TransactionCreateResult(response, false));
        }
        catch
        {
            await RollbackWithoutMaskingOriginalExceptionAsync(dbTransaction);
            throw;
        }
    }

    public async Task<Result<bool>> DeleteAsync(
        Guid portfolioId, Guid transactionId, CancellationToken cancellationToken)
    {
        await using var dbTransaction = await BeginMutationTransactionAsync(cancellationToken);
        try
        {
            var portfolio = await LockActivePortfolioAsync(portfolioId, cancellationToken);
            if (portfolio is null)
            {
                await RollbackAsync(dbTransaction);
                return Result<bool>.Failure("portfolio.not_found", "Portfolio was not found.");
            }

            var transaction = await _dbContext.PortfolioTransactions
                .SingleOrDefaultAsync(x => x.PortfolioId == portfolio.Id && x.Id == transactionId, cancellationToken);
            if (transaction is null)
            {
                await RollbackAsync(dbTransaction);
                return Result<bool>.Failure("transaction.not_found", "Transaction was not found.");
            }

            var remaining = await _dbContext.PortfolioTransactions
                .AsNoTracking()
                .Where(x => x.PortfolioId == portfolio.Id && x.Id != transaction.Id)
                .ToListAsync(cancellationToken);
            if (!FifoPortfolioCalculator.Calculate(remaining).IsValid)
            {
                await RollbackAsync(dbTransaction);
                return Result<bool>.Failure(
                    "transaction.delete_would_oversell",
                    "Deleting this transaction would make a later sell exceed available quantity.");
            }

            _transactionRepository.Remove(transaction);
            await _dbContext.SaveChangesAsync(cancellationToken);

            await RecalculateHoldingAsync(portfolio.Id, transaction.SecurityId, cancellationToken);
            await ReconcileDividendCashFlowsAsync(portfolio.Id, transaction.SecurityId, cancellationToken);
            await _dbContext.SaveChangesAsync(cancellationToken);

            await _portfolioFundingService.RebuildImplicitFundingAsync(portfolio.Id, cancellationToken);
            await _dbContext.SaveChangesAsync(cancellationToken);

            await CommitAsync(dbTransaction, cancellationToken);
            return Result<bool>.Success(true);
        }
        catch
        {
            await RollbackWithoutMaskingOriginalExceptionAsync(dbTransaction);
            throw;
        }
    }

    public async Task<Result<bool>> DeleteAllBySecurityAsync(
        Guid portfolioId, Guid securityId, CancellationToken cancellationToken)
    {
        await using var dbTransaction = await BeginMutationTransactionAsync(cancellationToken);
        try
        {
            var portfolio = await LockActivePortfolioAsync(portfolioId, cancellationToken);
            if (portfolio is null)
            {
                await RollbackAsync(dbTransaction);
                return Result<bool>.Failure("portfolio.not_found", "Portfolio was not found.");
            }

            var transactions = await _dbContext.PortfolioTransactions
                .Where(x => x.PortfolioId == portfolio.Id && x.SecurityId == securityId)
                .ToListAsync(cancellationToken);
            _dbContext.PortfolioTransactions.RemoveRange(transactions);

            var holding = await _dbContext.PortfolioHoldings
                .SingleOrDefaultAsync(x => x.PortfolioId == portfolio.Id && x.SecurityId == securityId, cancellationToken);
            if (holding is not null)
                _dbContext.PortfolioHoldings.Remove(holding);

            await _dbContext.SaveChangesAsync(cancellationToken);

            await RecalculateHoldingAsync(portfolio.Id, securityId, cancellationToken);
            await ReconcileDividendCashFlowsAsync(portfolio.Id, securityId, cancellationToken);
            await _dbContext.SaveChangesAsync(cancellationToken);

            await _portfolioFundingService.RebuildImplicitFundingAsync(portfolio.Id, cancellationToken);
            await _dbContext.SaveChangesAsync(cancellationToken);

            await CommitAsync(dbTransaction, cancellationToken);
            return Result<bool>.Success(true);
        }
        catch
        {
            await RollbackWithoutMaskingOriginalExceptionAsync(dbTransaction);
            throw;
        }
    }

    private async Task<IDbContextTransaction?> BeginMutationTransactionAsync(CancellationToken cancellationToken)
    {
        if (!_dbContext.Database.IsRelational())
            return null;

        return await _dbContext.Database.BeginTransactionAsync(cancellationToken);
    }

    private async Task<Portfolio?> LockActivePortfolioAsync(
        Guid portfolioId, CancellationToken cancellationToken)
    {
        if (!_dbContext.Database.IsRelational())
        {
            return await _dbContext.Portfolios.SingleOrDefaultAsync(
                x => x.Id == portfolioId
                    && x.OwnerUserId == _currentUser.UserId
                    && x.IsActive,
                cancellationToken);
        }

        return await _dbContext.Portfolios
            .FromSqlInterpolated($"SELECT * FROM portfolio WHERE id = {portfolioId} AND owner_user_id = {_currentUser.UserId} AND is_active = TRUE FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken);
    }

    private async Task RecalculateHoldingAsync(
        Guid portfolioId, Guid securityId, CancellationToken cancellationToken)
    {
        var transactions = await _transactionRepository.ListByHoldingAsync(portfolioId, securityId, cancellationToken);
        var fifo = FifoPortfolioCalculator.Calculate(transactions);
        if (!fifo.IsValid)
            throw new InvalidOperationException(fifo.Error ?? "Portfolio transaction FIFO state is invalid.");

        var lots = fifo.LotsBySecurity.GetValueOrDefault(securityId, new Queue<FifoLot>());
        var netQuantity = lots.Sum(x => x.Quantity);
        var holding = await _holdingRepository.GetBySecurityIdAsync(portfolioId, securityId, cancellationToken);

        if (netQuantity <= 0)
        {
            if (holding is not null)
                _holdingRepository.Remove(holding);
            return;
        }

        var avgCost = lots.Sum(x => x.CostValue) / netQuantity;
        if (holding is null)
        {
            var security = await _securityRepository.GetEntityAsync(securityId, cancellationToken);
            if (security is null)
                throw new InvalidOperationException("Security for the holding was not found.");

            _holdingRepository.Add(new PortfolioHolding
            {
                PortfolioId = portfolioId,
                SecurityId = securityId,
                Quantity = netQuantity,
                AverageCost = avgCost,
                CostCurrency = security.Currency ?? "TWD",
                UpdatedAtUtc = DateTime.UtcNow
            });
        }
        else
        {
            holding.Quantity = netQuantity;
            holding.AverageCost = avgCost;
            holding.UpdatedAtUtc = DateTime.UtcNow;
        }
    }

    private async Task ReconcileDividendCashFlowsAsync(
        Guid portfolioId, Guid securityId, CancellationToken cancellationToken)
    {
        var events = await _dbContext.CashDividendEvents
            .AsNoTracking()
            .Where(x => x.SecurityId == securityId)
            .OrderBy(x => x.ExDividendDate)
            .ThenBy(x => x.Id)
            .ToListAsync(cancellationToken);

        var transactions = await _dbContext.PortfolioTransactions
            .AsNoTracking()
            .Where(x => x.PortfolioId == portfolioId && x.SecurityId == securityId)
            .ToListAsync(cancellationToken);

        var existingFlows = await _dbContext.PortfolioCashFlows
            .Where(x => x.PortfolioId == portfolioId
                && x.SecurityId == securityId
                && x.FlowType == "Dividend"
                && x.CashDividendEventId.HasValue)
            .ToListAsync(cancellationToken);
        var flowsByEvent = existingFlows
            .Where(x => x.CashDividendEventId.HasValue)
            .ToDictionary(x => x.CashDividendEventId!.Value);
        var eventIds = events.Select(x => x.Id).ToHashSet();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        foreach (var flow in existingFlows)
        {
            if (!eventIds.Contains(flow.CashDividendEventId!.Value)
                && !IsUserPreservedDividendFlow(flow))
            {
                _dbContext.PortfolioCashFlows.Remove(flow);
            }
        }

        foreach (var dividendEvent in events)
        {
            var quantity = transactions
                .Where(x => x.TransactionDate <= dividendEvent.ExDividendDate)
                .Sum(x => x.TransactionType == "BUY" ? x.Quantity : -x.Quantity);
            flowsByEvent.TryGetValue(dividendEvent.Id, out var flow);

            if (quantity <= 0)
            {
                if (flow is not null && !IsUserPreservedDividendFlow(flow))
                    _dbContext.PortfolioCashFlows.Remove(flow);
                continue;
            }

            var effectiveDate = dividendEvent.PaymentDate ?? dividendEvent.ExDividendDate;
            var amount = quantity * dividendEvent.CashAmountPerShare;
            if (flow is null)
            {
                _dbContext.PortfolioCashFlows.Add(new PortfolioCashFlow
                {
                    PortfolioId = portfolioId,
                    SecurityId = securityId,
                    CashDividendEventId = dividendEvent.Id,
                    FlowType = "Dividend",
                    Amount = amount,
                    Currency = dividendEvent.Currency,
                    EffectiveDate = effectiveDate,
                    Status = AutoStatus(effectiveDate, today),
                    IsUserAdjusted = false,
                    IsSystemDerived = true,
                    Note = AutomaticDividendNote
                });
                continue;
            }

            if (IsUserPreservedDividendFlow(flow))
                continue;

            flow.SecurityId = securityId;
            flow.FlowType = "Dividend";
            flow.Amount = amount;
            flow.Currency = dividendEvent.Currency;
            flow.EffectiveDate = effectiveDate;
            flow.Status = AutoStatus(effectiveDate, today);
            flow.IsUserAdjusted = false;
            flow.IsSystemDerived = true;
            flow.Note = AutomaticDividendNote;
        }
    }

    private static bool IsUserPreservedDividendFlow(PortfolioCashFlow flow) =>
        flow.IsUserAdjusted || string.Equals(flow.Status, "Skipped", StringComparison.OrdinalIgnoreCase);

    private static string AutoStatus(DateOnly effectiveDate, DateOnly today) =>
        effectiveDate <= today ? "Posted" : "Scheduled";

    private async Task<TransactionResponse> BuildResponseAsync(
        PortfolioTransaction transaction,
        Security security,
        CancellationToken cancellationToken)
    {
        var transactions = await _dbContext.PortfolioTransactions
            .AsNoTracking()
            .Where(x => x.PortfolioId == transaction.PortfolioId)
            .ToListAsync(cancellationToken);
        var fifo = FifoPortfolioCalculator.Calculate(transactions);
        return ToResponse(
            transaction,
            security,
            fifo.Sales.TryGetValue(transaction.Id, out var sale) ? sale : null);
    }

    private static TransactionResponse ToResponse(
        PortfolioTransaction transaction,
        Security security,
        FifoSale? sale)
    {
        return new TransactionResponse(
            transaction.Id,
            transaction.SecurityId,
            security.Ticker,
            security.Exchange,
            security.Name,
            transaction.TransactionType,
            transaction.Quantity,
            transaction.Price,
            transaction.Fee,
            transaction.TransactionDate,
            transaction.Note,
            transaction.CreatedAtUtc,
            sale?.NetProceeds,
            sale?.MatchedCost,
            sale?.RealizedPnl);
    }

    private static bool TryNormalizeIdempotencyKey(
        string? rawKey,
        out string normalizedKey,
        out ApiError? error)
    {
        normalizedKey = rawKey?.Trim() ?? string.Empty;
        if (normalizedKey.Length == 0)
        {
            error = new ApiError(
                "transaction.idempotency_key_required",
                "The Idempotency-Key header is required.");
            return false;
        }

        if (normalizedKey.Length > 128)
        {
            error = new ApiError(
                "transaction.idempotency_key_invalid",
                "The Idempotency-Key must be between 1 and 128 characters after trimming.");
            return false;
        }

        error = null;
        return true;
    }

    private static bool TryNormalizeRequest(
        CreateTransactionRequest request,
        out NormalizedTransactionRequest normalized,
        out ApiError? error)
    {
        var transactionType = request.TransactionType?.Trim().ToUpperInvariant();
        if (transactionType is not ("BUY" or "SELL"))
        {
            normalized = default!;
            error = new ApiError(
                "transaction.invalid_type",
                "Transaction type must be BUY or SELL.");
            return false;
        }

        if (request.Quantity <= 0)
        {
            normalized = default!;
            error = new ApiError(
                "transaction.invalid_quantity",
                "Quantity must be greater than zero.");
            return false;
        }

        if (request.Price < 0)
        {
            normalized = default!;
            error = new ApiError(
                "transaction.invalid_price",
                "Price cannot be negative.");
            return false;
        }

        var fee = request.Fee ?? 0m;
        if (fee < 0)
        {
            normalized = default!;
            error = new ApiError(
                "transaction.invalid_fee",
                "Fee cannot be negative.");
            return false;
        }

        normalized = new NormalizedTransactionRequest(
            request.SecurityId,
            transactionType,
            request.Quantity,
            request.Price,
            fee,
            request.TransactionDate,
            string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim());
        error = null;
        return true;
    }

    private static string ComputeRequestHash(NormalizedTransactionRequest request)
    {
        var canonical = JsonSerializer.Serialize(new
        {
            securityId = request.SecurityId.ToString("D"),
            transactionType = request.TransactionType,
            quantity = request.Quantity,
            price = request.Price,
            fee = request.Fee,
            transactionDate = request.TransactionDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            note = request.Note
        });
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
    }

    private static async Task CommitAsync(IDbContextTransaction? transaction, CancellationToken cancellationToken)
    {
        if (transaction is not null)
            await transaction.CommitAsync(cancellationToken);
    }

    private static async Task RollbackAsync(IDbContextTransaction? transaction)
    {
        if (transaction is not null)
            await transaction.RollbackAsync(CancellationToken.None);
    }

    private static async Task RollbackWithoutMaskingOriginalExceptionAsync(IDbContextTransaction? transaction)
    {
        if (transaction is null)
            return;

        try
        {
            await transaction.RollbackAsync(CancellationToken.None);
        }
        catch
        {
            // Preserve the original database/service exception.
        }
    }

    private sealed record NormalizedTransactionRequest(
        Guid SecurityId,
        string TransactionType,
        decimal Quantity,
        decimal Price,
        decimal Fee,
        DateOnly TransactionDate,
        string? Note);
}
