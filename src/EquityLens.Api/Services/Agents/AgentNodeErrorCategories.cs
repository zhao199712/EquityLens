namespace EquityLens.Api.Services.Agents;

public static class AgentNodeErrorCategories
{
    public const string TransientFailure = "TransientFailure";
    public const string PermanentFailure = "PermanentFailure";
    public const string ValidationFailure = "ValidationFailure";
    public const string PolicyRejection = "PolicyRejection";
    public const string TimedOut = "TimedOut";
    public const string Cancelled = "Cancelled";
    public const string InsufficientEvidence = "InsufficientEvidence";
}
