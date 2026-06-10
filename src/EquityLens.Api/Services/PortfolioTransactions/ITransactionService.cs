using EquityLens.Api.Common;
using EquityLens.Api.Contracts.PortfolioTransactions;

namespace EquityLens.Api.Services.PortfolioTransactions;

public interface ITransactionService
{
    Task<Result<IReadOnlyList<TransactionResponse>>> ListAsync(Guid portfolioId, Guid? securityId, CancellationToken cancellationToken);
    Task<Result<TransactionResponse>> CreateAsync(Guid portfolioId, CreateTransactionRequest request, CancellationToken cancellationToken);
    Task<Result<bool>> DeleteAsync(Guid portfolioId, Guid transactionId, CancellationToken cancellationToken);
    Task<Result<bool>> DeleteAllBySecurityAsync(Guid portfolioId, Guid securityId, CancellationToken cancellationToken);
}
