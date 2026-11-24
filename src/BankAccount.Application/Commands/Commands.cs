namespace BankAccount.Application.Commands;

public sealed record OpenAccountCommand(string Owner, string Currency);

public sealed record DepositCommand(Guid AccountId, decimal Amount, string Description);

public sealed record WithdrawCommand(Guid AccountId, decimal Amount, string Description);

public sealed record TransferCommand(Guid FromAccountId, Guid ToAccountId, decimal Amount, string Description);

public sealed record CloseAccountCommand(Guid AccountId);

/// <summary>State of the account after a command, as the client needs it.</summary>
public sealed record AccountChanged(Guid AccountId, decimal Balance, string Currency, int Version);

public sealed record TransferCompleted(Guid TransferId, AccountChanged From, AccountChanged To);
