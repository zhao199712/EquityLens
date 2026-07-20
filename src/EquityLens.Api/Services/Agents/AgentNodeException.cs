namespace EquityLens.Api.Services.Agents;

public sealed class AgentNodeException : Exception
{
    public string ErrorCode { get; }
    public string ErrorCategory { get; }
    public bool Retryable { get; }

    public AgentNodeException(
        string errorCode,
        string errorCategory,
        string message,
        bool retryable = false,
        Exception? innerException = null)
        : base(message, innerException)
    {
        ErrorCode = errorCode;
        ErrorCategory = errorCategory;
        Retryable = retryable;
    }
}
