namespace KlavLor.Application.Common;

public abstract class Result(bool isSuccess, string error, IDictionary<string, string[]>? validationErrors = null)
{
    public bool IsSuccess { get; } = isSuccess;
    public string ErrorMessage { get; } = error;
    public IDictionary<string, string[]>? ValidationErrors { get; } = validationErrors;

    public static Result Success() => new Result<NoValue>(NoValue.Instance, true, string.Empty);
    public static Result Failure(string error) => new Result<NoValue>(NoValue.Instance, false, error);
    public static Result ValidationFailure(IDictionary<string, string[]> errors) =>
        new Result<NoValue>(NoValue.Instance, false, "Validation failed", errors);
}

public class Result<T>(T value, bool isSuccess, string error, IDictionary<string, string[]>? validationErrors = null)
    : Result(isSuccess, error, validationErrors)
{
    public T Value { get; } = value;

    public static Result<T> Success(T value) => new(value, true, string.Empty);
    public new static Result<T> Failure(string error) => new(default!, false, error);
    public new static Result<T> ValidationFailure(IDictionary<string, string[]> errors) =>
        new(default!, false, "Validation failed", errors);
}

public readonly struct NoValue
{
    public static readonly NoValue Instance = new();
}
