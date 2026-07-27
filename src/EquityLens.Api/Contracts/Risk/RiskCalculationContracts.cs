using System.Text.Json;

namespace EquityLens.Api.Contracts.Risk;

public sealed record CreateRiskCalculationRequest(
    string Operation,
    DateOnly? From = null,
    DateOnly? To = null,
    int Simulations = 10000,
    JsonElement? Parameters = null);

public sealed record RiskCalculationRunResponse(
    Guid Id,
    Guid PortfolioId,
    string Operation,
    string Status,
    int ProgressPercent,
    string RequestedModel,
    string? SelectedModel,
    string AlgorithmVersion,
    string? InputHash,
    string? DataFactorVersion,
    string? FallbackReason,
    int FallbackDepth,
    DateTime CreatedAtUtc,
    DateTime? StartedAtUtc,
    DateTime? CompletedAtUtc,
    string? ErrorCode,
    string? ErrorMessage,
    JsonElement? Result);
