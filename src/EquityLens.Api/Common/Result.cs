namespace EquityLens.Api.Common;

public sealed class Result<T>
{
    private Result(T? value, string? errorCode, string? errorMessage)
    {
        Value = value;
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
    }

    public T? Value { get; }
    public string? ErrorCode { get; }
    public string? ErrorMessage { get; }
    public bool IsSuccess => ErrorCode is null;

    public static Result<T> Success(T value) => new(value, null, null);

    public static Result<T> Failure(string errorCode, string errorMessage) => new(default, errorCode, errorMessage);
}

public sealed record ApiError(string Code, string Message);
