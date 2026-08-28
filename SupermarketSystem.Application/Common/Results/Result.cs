namespace SupermarketSystem.Application.Common.Results;

/// <summary>
/// How a failure is classified. This is what lets the API layer map an
/// application failure to the right HTTP status without the handler knowing
/// anything about HTTP — the Application layer stays transport-agnostic.
/// </summary>
public enum ErrorType
{
    Validation = 1,
    NotFound = 2,
    Conflict = 3,
    /// <summary>Denied by policy/permission. Distinct from Validation: the request was well-formed, the caller just isn't allowed.</summary>
    Forbidden = 4,
    /// <summary>A business rule/invariant was violated (e.g. insufficient stock, over-payment).</summary>
    BusinessRule = 5,
    /// <summary>Optimistic concurrency conflict (DbUpdateConcurrencyException translated).</summary>
    Concurrency = 6
}

/// <summary>
/// Code is a stable machine-readable identifier (never a localized string) so
/// clients and logs can branch on it; Message is human-facing.
/// </summary>
public sealed record Error(string Code, string Message, ErrorType Type)
{
    public static Error Validation(string code, string message) => new(code, message, ErrorType.Validation);
    public static Error NotFound(string code, string message) => new(code, message, ErrorType.NotFound);
    public static Error Conflict(string code, string message) => new(code, message, ErrorType.Conflict);
    public static Error Forbidden(string code, string message) => new(code, message, ErrorType.Forbidden);
    public static Error BusinessRule(string code, string message) => new(code, message, ErrorType.BusinessRule);
    public static Error Concurrency(string code, string message) => new(code, message, ErrorType.Concurrency);
}

/// <summary>
/// Explicit success/failure, used instead of throwing for *expected* outcomes
/// (denied by policy, insufficient stock, duplicate submission). Exceptions
/// stay reserved for genuinely exceptional conditions — a POS denying a
/// discount is not exceptional, it's a normal Tuesday.
/// </summary>
public class Result
{
    public bool IsSuccess { get; }
    public Error? Error { get; }
    public bool IsFailure => !IsSuccess;

    protected Result(bool isSuccess, Error? error)
    {
        if (isSuccess && error is not null)
        {
            throw new InvalidOperationException("A successful result cannot carry an error.");
        }

        if (!isSuccess && error is null)
        {
            throw new InvalidOperationException("A failed result must carry an error.");
        }

        IsSuccess = isSuccess;
        Error = error;
    }

    public static Result Success() => new(true, null);
    public static Result Failure(Error error) => new(false, error);
    public static Result<TValue> Success<TValue>(TValue value) => new(value, true, null);
    public static Result<TValue> Failure<TValue>(Error error) => new(default, false, error);
}

public class Result<TValue> : Result
{
    private readonly TValue? _value;

    internal Result(TValue? value, bool isSuccess, Error? error) : base(isSuccess, error)
    {
        _value = value;
    }

    public TValue Value => IsSuccess
        ? _value!
        : throw new InvalidOperationException("Cannot access the value of a failed result.");
}
