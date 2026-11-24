using BankAccount.Application.Abstractions;
using BankAccount.Domain;
using Microsoft.Extensions.Logging;

namespace BankAccount.Application.Commands;

/// <summary>
/// Every command follows the same steps: read the stream, rebuild the aggregate, run the command, append the new events
/// with the version that was read. If another writer appended first, the whole command runs again on the fresh stream,
/// so its business rules are checked against the real balance. Nothing is decided on stale data.
/// </summary>
public sealed partial class AccountCommandHandler(IEventStore store, TimeProvider clock, ILogger<AccountCommandHandler> logger)
{
    public const int MaxAttempts = 3;

    public async Task<Result<AccountChanged>> OpenAsync(OpenAccountCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (InputRules.Check(
                ("owner", InputRules.Text(command.Owner, Account.MaxOwnerLength)),
                ("currency", InputRules.Currency(command.Currency))) is { } invalid)
        {
            return invalid;
        }

        var account = Account.Open(Guid.CreateVersion7(), command.Owner, command.Currency, clock.GetUtcNow());
        await store.AppendAsync([new StreamAppend(account.Id, 0, account.PendingEvents)], cancellationToken);
        return Result.Success(Changed(account));
    }

    public Task<Result<AccountChanged>> DepositAsync(DepositCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        return InputRules.Check(
                ("amount", InputRules.Amount(command.Amount)),
                ("description", InputRules.Text(command.Description, Account.MaxDescriptionLength))) is { } invalid
            ? Task.FromResult<Result<AccountChanged>>(invalid)
            : ChangeOneAsync(command.AccountId, account => account.Deposit(command.Amount, command.Description, clock.GetUtcNow()), cancellationToken);
    }

    public Task<Result<AccountChanged>> WithdrawAsync(WithdrawCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        return InputRules.Check(
                ("amount", InputRules.Amount(command.Amount)),
                ("description", InputRules.Text(command.Description, Account.MaxDescriptionLength))) is { } invalid
            ? Task.FromResult<Result<AccountChanged>>(invalid)
            : ChangeOneAsync(command.AccountId, account => account.Withdraw(command.Amount, command.Description, clock.GetUtcNow()), cancellationToken);
    }

    public Task<Result<AccountChanged>> CloseAsync(CloseAccountCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        return ChangeOneAsync(command.AccountId, account => account.Close(clock.GetUtcNow()), cancellationToken);
    }

    public async Task<Result<TransferCompleted>> TransferAsync(TransferCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (InputRules.Check(
                ("amount", InputRules.Amount(command.Amount)),
                ("description", InputRules.Text(command.Description, Account.MaxDescriptionLength)),
                ("toAccountId", command.FromAccountId == command.ToAccountId ? "Must be different from the source account." : null)) is { } invalid)
        {
            return invalid;
        }

        var transferId = Guid.CreateVersion7();
        for (var attempt = 1; ; attempt++)
        {
            var source = await LoadAsync(command.FromAccountId, cancellationToken);
            var destination = await LoadAsync(command.ToAccountId, cancellationToken);
            if (source is null || destination is null)
            {
                return NotFound(source is null ? command.FromAccountId : command.ToAccountId);
            }

            try
            {
                source.SendTransfer(transferId, destination, command.Amount, command.Description, clock.GetUtcNow());
            }
            catch (DomainException ex)
            {
                return new Failure(FailureKind.BusinessRule, ex.Message);
            }

            try
            {
                // Both streams in one transaction: the money leaves one account only if it arrives in the other.
                await store.AppendAsync(
                    [
                        new StreamAppend(source.Id, source.Version, source.PendingEvents),
                        new StreamAppend(destination.Id, destination.Version, destination.PendingEvents),
                    ],
                    cancellationToken);
                return Result.Success(new TransferCompleted(transferId, Changed(source), Changed(destination)));
            }
            catch (ConcurrencyConflictException ex) when (attempt < MaxAttempts)
            {
                LogRetry(ex.AccountId, attempt);
            }
            catch (ConcurrencyConflictException)
            {
                return Conflict();
            }
        }
    }

    private async Task<Result<AccountChanged>> ChangeOneAsync(Guid accountId, Action<Account> change, CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            var account = await LoadAsync(accountId, cancellationToken);
            if (account is null)
            {
                return NotFound(accountId);
            }

            try
            {
                change(account);
            }
            catch (DomainException ex)
            {
                return new Failure(FailureKind.BusinessRule, ex.Message);
            }

            try
            {
                await store.AppendAsync([new StreamAppend(account.Id, account.Version, account.PendingEvents)], cancellationToken);
                return Result.Success(Changed(account));
            }
            catch (ConcurrencyConflictException) when (attempt < MaxAttempts)
            {
                LogRetry(accountId, attempt);
            }
            catch (ConcurrencyConflictException)
            {
                return Conflict();
            }
        }
    }

    private async Task<Account?> LoadAsync(Guid accountId, CancellationToken cancellationToken)
    {
        var history = await store.ReadStreamAsync(accountId, until: null, cancellationToken);
        return history.Count == 0 ? null : Account.FromHistory(history);
    }

    private static AccountChanged Changed(Account account) =>
        new(account.Id, account.Balance.Amount, account.Currency, account.Version + account.PendingEvents.Count);

    private static Failure NotFound(Guid accountId) => new(FailureKind.NotFound, $"Account {accountId} was not found.");

    private static Failure Conflict() =>
        new(FailureKind.Conflict, $"The account kept changing; the command was retried {MaxAttempts} times. Try again.");

    [LoggerMessage(Level = LogLevel.Information, Message = "Stream {AccountId} changed during attempt {Attempt}; running the command again")]
    private partial void LogRetry(Guid accountId, int attempt);
}
