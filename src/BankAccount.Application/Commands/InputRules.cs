using BankAccount.Domain;

namespace BankAccount.Application.Commands;

/// <summary>Shape checks that answer 400 with every invalid field. The domain still guards the same rules.</summary>
internal static class InputRules
{
    public static Failure? Check(params (string Field, string? Error)[] checks)
    {
        var errors = checks.Where(c => c.Error is not null).ToDictionary(c => c.Field, c => new[] { c.Error! });
        return errors.Count == 0 ? null : new Failure(FailureKind.Validation, "One or more fields are invalid.", errors);
    }

    public static string? Amount(decimal amount) =>
        amount <= 0 || decimal.Round(amount, 2) != amount ? "Must be greater than zero with at most two decimal places." : null;

    public static string? Text(string? value, int maxLength) =>
        string.IsNullOrWhiteSpace(value) || value.Trim().Length > maxLength ? $"Required, at most {maxLength} characters." : null;

    public static string? Currency(string? value) =>
        Money.IsSupportedCurrency(value) ? null : $"Must be one of: {string.Join(", ", Money.SupportedCurrencies)}.";
}
