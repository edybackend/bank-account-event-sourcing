namespace BankAccount.Domain;

// Events are the source of truth and the stored contract, so they carry only primitives:
// renaming a class or changing a value object must never make old events unreadable.

/// <summary>Something that happened to an account. The stream of these events is the account.</summary>
public abstract record AccountEvent(Guid AccountId, DateTimeOffset OccurredAt);

public sealed record AccountOpened(Guid AccountId, string Owner, string Currency, decimal DailyWithdrawalLimit, DateTimeOffset OccurredAt)
    : AccountEvent(AccountId, OccurredAt);

public sealed record MoneyDeposited(Guid AccountId, decimal Amount, string Description, DateTimeOffset OccurredAt)
    : AccountEvent(AccountId, OccurredAt);

public sealed record MoneyWithdrawn(Guid AccountId, decimal Amount, string Description, DateTimeOffset OccurredAt)
    : AccountEvent(AccountId, OccurredAt);

public sealed record TransferSent(Guid AccountId, Guid TransferId, Guid ToAccountId, decimal Amount, string Description, DateTimeOffset OccurredAt)
    : AccountEvent(AccountId, OccurredAt);

public sealed record TransferReceived(Guid AccountId, Guid TransferId, Guid FromAccountId, decimal Amount, string Description, DateTimeOffset OccurredAt)
    : AccountEvent(AccountId, OccurredAt);

public sealed record AccountClosed(Guid AccountId, DateTimeOffset OccurredAt)
    : AccountEvent(AccountId, OccurredAt);
