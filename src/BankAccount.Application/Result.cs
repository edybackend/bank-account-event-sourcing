namespace BankAccount.Application;

public enum FailureKind
{
    /// <summary>The request is malformed (400).</summary>
    Validation,

    /// <summary>The account does not exist (404).</summary>
    NotFound,

    /// <summary>The stream kept changing under the command (409).</summary>
    Conflict,

    /// <summary>The request is well formed but breaks a business rule, such as insufficient funds (422).</summary>
    BusinessRule,
}

/// <summary>Why a use case did not succeed. <see cref="Fields"/> lists errors per field for validation failures.</summary>
public sealed record Failure(FailureKind Kind, string Message, IReadOnlyDictionary<string, string[]>? Fields = null);

/// <summary>Outcome of a use case: either a value or a <see cref="Application.Failure"/>. Expected failures are not exceptions.</summary>
public sealed class Result<T>
{
    internal Result(T? value, Failure? failure)
    {
        Value = value;
        Failure = failure;
    }

    public T? Value { get; }

    public Failure? Failure { get; }

    public bool IsSuccess => Failure is null;

    public static implicit operator Result<T>(Failure failure) => new(default, failure);
}

public static class Result
{
    public static Result<T> Success<T>(T value) => new(value, null);
}
