namespace BankAccount.Domain;

/// <summary>An amount in a currency. Two decimal places, never mixed across currencies.</summary>
public sealed record Money : IComparable<Money>
{
    public static readonly IReadOnlySet<string> SupportedCurrencies = new HashSet<string>(StringComparer.Ordinal) { "BRL", "USD", "EUR" };

    private Money(decimal amount, string currency)
    {
        Amount = amount;
        Currency = currency;
    }

    public decimal Amount { get; }

    public string Currency { get; }

    public static Money Zero(string currency) => new(0m, NormalizeCurrency(currency));

    /// <summary>Creates a positive amount, as used by deposits, withdrawals and transfers.</summary>
    public static Money Positive(decimal amount, string currency)
    {
        if (amount <= 0)
        {
            throw new DomainException("Amount must be greater than zero.");
        }

        if (decimal.Round(amount, 2) != amount)
        {
            throw new DomainException("Amount can have at most two decimal places.");
        }

        return new Money(amount, NormalizeCurrency(currency));
    }

    public static bool IsSupportedCurrency(string? currency) =>
        currency is not null && SupportedCurrencies.Contains(currency.Trim().ToUpperInvariant());

    public Money Add(Money other) => new(Amount + SameCurrency(other).Amount, Currency);

    public Money Subtract(Money other) => new(Amount - SameCurrency(other).Amount, Currency);

    public int CompareTo(Money? other) => other is null ? 1 : Amount.CompareTo(SameCurrency(other).Amount);

    public static bool operator >(Money left, Money right) => left?.CompareTo(right) > 0;

    public static bool operator <(Money left, Money right) => left?.CompareTo(right) < 0;

    public static bool operator >=(Money left, Money right) => left?.CompareTo(right) >= 0;

    public static bool operator <=(Money left, Money right) => left?.CompareTo(right) <= 0;

    public override string ToString() => $"{Currency} {Amount:0.00}";

    private Money SameCurrency(Money other)
    {
        ArgumentNullException.ThrowIfNull(other);
        if (other.Currency != Currency)
        {
            throw new DomainException($"Cannot combine {Currency} with {other.Currency}.");
        }

        return other;
    }

    private static string NormalizeCurrency(string currency)
    {
        var code = currency?.Trim().ToUpperInvariant() ?? string.Empty;
        if (!SupportedCurrencies.Contains(code))
        {
            throw new DomainException($"Currency must be one of: {string.Join(", ", SupportedCurrencies)}.");
        }

        return code;
    }
}
