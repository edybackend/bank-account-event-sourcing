using BankAccount.Application.Abstractions;
using BankAccount.Domain;

namespace BankAccount.Application.Queries;

/// <summary>Read side: projections built from the events, queried directly.</summary>
public interface IAccountReadModel
{
    Task<IReadOnlyList<AccountSummary>> ListAsync(CancellationToken cancellationToken);

    Task<AccountSummary?> GetAsync(Guid accountId, CancellationToken cancellationToken);

    Task<PagedResult<StatementLine>> GetStatementAsync(Guid accountId, PageRequest page, CancellationToken cancellationToken);
}

public sealed record AccountSummary(Guid Id, string Owner, string Currency, decimal Balance, string Status, int Version, DateTimeOffset OpenedAt);

public sealed record StatementLine(long Position, string Type, decimal Amount, decimal BalanceAfter, string Description, Guid? CounterpartyAccountId, DateTimeOffset OccurredAt);

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount);

public sealed record PageRequest
{
    public const int MaxPageSize = 100;

    public PageRequest(int? page, int? pageSize)
    {
        Page = Math.Max(page ?? 1, 1);
        PageSize = Math.Clamp(pageSize ?? 20, 1, MaxPageSize);
    }

    public int Page { get; }

    public int PageSize { get; }

    public int Skip => (Page - 1) * PageSize;
}

public sealed record BalanceAt(Guid AccountId, string Currency, decimal Balance, DateTimeOffset At, int EventsReplayed);

/// <summary>Answers from the event stream itself: what the account looked like at any moment.</summary>
public sealed class AccountHistory(IEventStore store, TimeProvider clock)
{
    public async Task<Result<BalanceAt>> GetBalanceAtAsync(Guid accountId, DateTimeOffset? at, CancellationToken cancellationToken)
    {
        var moment = at ?? clock.GetUtcNow();
        var history = await store.ReadStreamAsync(accountId, moment, cancellationToken);
        if (history.Count == 0)
        {
            return new Failure(FailureKind.NotFound, $"Account {accountId} did not exist at {moment:O}.");
        }

        var account = Account.FromHistory(history);
        return Result.Success(new BalanceAt(accountId, account.Currency, account.Balance.Amount, moment, history.Count));
    }

    public async Task<IReadOnlyList<AccountEvent>?> GetEventsAsync(Guid accountId, CancellationToken cancellationToken)
    {
        var history = await store.ReadStreamAsync(accountId, until: null, cancellationToken);
        return history.Count == 0 ? null : history;
    }
}
