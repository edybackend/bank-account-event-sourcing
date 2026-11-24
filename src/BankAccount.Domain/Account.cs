namespace BankAccount.Domain;

/// <summary>
/// Event-sourced aggregate. Commands check the rules against the current state and raise events;
/// <see cref="Apply"/> is the only place where state changes, so replaying the stream rebuilds the same state.
/// </summary>
public sealed class Account
{
    public const decimal DefaultDailyWithdrawalLimit = 5_000m;
    public const int MaxOwnerLength = 100;
    public const int MaxDescriptionLength = 140;

    private readonly List<AccountEvent> _pendingEvents = [];
    private DateOnly _withdrawalDay;
    private Money _withdrawnOnThatDay = null!;

    private Account()
    {
    }

    public Guid Id { get; private set; }

    public string Owner { get; private set; } = string.Empty;

    public string Currency { get; private set; } = string.Empty;

    public Money Balance { get; private set; } = null!;

    public Money DailyWithdrawalLimit { get; private set; } = null!;

    public bool IsClosed { get; private set; }

    /// <summary>Number of events already stored. The event store appends only if the stream is still at this version.</summary>
    public int Version { get; private set; }

    /// <summary>Events raised by commands and not yet stored.</summary>
    public IReadOnlyList<AccountEvent> PendingEvents => _pendingEvents.AsReadOnly();

    public static Account Open(Guid id, string owner, string currency, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(owner) || owner.Trim().Length > MaxOwnerLength)
        {
            throw new DomainException($"Owner is required and must have at most {MaxOwnerLength} characters.");
        }

        var opening = Money.Zero(currency); // validates the currency
        var account = new Account();
        account.Raise(new AccountOpened(id, owner.Trim(), opening.Currency, DefaultDailyWithdrawalLimit, now));
        return account;
    }

    /// <summary>Rebuilds the account by replaying its stored events, in order.</summary>
    public static Account FromHistory(IEnumerable<AccountEvent> history)
    {
        ArgumentNullException.ThrowIfNull(history);

        var account = new Account();
        foreach (var @event in history)
        {
            account.Apply(@event);
            account.Version++;
        }

        if (account.Version == 0)
        {
            throw new ArgumentException("An account stream must have at least one event.", nameof(history));
        }

        return account;
    }

    public void Deposit(decimal amount, string description, DateTimeOffset now)
    {
        EnsureOpen();
        var money = Money.Positive(amount, Currency);
        Raise(new MoneyDeposited(Id, money.Amount, ValidDescription(description), now));
    }

    public void Withdraw(decimal amount, string description, DateTimeOffset now)
    {
        var money = EnsureCanTakeOut(amount, now);
        Raise(new MoneyWithdrawn(Id, money.Amount, ValidDescription(description), now));
    }

    public void SendTransfer(Guid transferId, Account destination, decimal amount, string description, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(destination);

        if (destination.Id == Id)
        {
            throw new DomainException("Cannot transfer to the same account.");
        }

        if (destination.Currency != Currency)
        {
            throw new DomainException($"Cannot transfer {Currency} to a {destination.Currency} account.");
        }

        var money = EnsureCanTakeOut(amount, now);
        var text = ValidDescription(description);
        destination.EnsureOpen();

        Raise(new TransferSent(Id, transferId, destination.Id, money.Amount, text, now));
        destination.Raise(new TransferReceived(destination.Id, transferId, Id, money.Amount, text, now));
    }

    public void Close(DateTimeOffset now)
    {
        EnsureOpen();
        if (Balance.Amount != 0)
        {
            throw new DomainException($"Only an account with zero balance can be closed; the balance is {Balance}.");
        }

        Raise(new AccountClosed(Id, now));
    }

    private Money EnsureCanTakeOut(decimal amount, DateTimeOffset now)
    {
        EnsureOpen();
        var money = Money.Positive(amount, Currency);

        if (money > Balance)
        {
            throw new DomainException($"Insufficient funds: the balance is {Balance}.");
        }

        var day = DateOnly.FromDateTime(now.UtcDateTime);
        var alreadyToday = day == _withdrawalDay ? _withdrawnOnThatDay : Money.Zero(Currency);
        if (alreadyToday.Add(money) > DailyWithdrawalLimit)
        {
            throw new DomainException(
                $"Daily limit of {DailyWithdrawalLimit} exceeded: {alreadyToday} already taken out today.");
        }

        return money;
    }

    private void EnsureOpen()
    {
        if (IsClosed)
        {
            throw new DomainException("The account is closed.");
        }
    }

    private static string ValidDescription(string description)
    {
        if (string.IsNullOrWhiteSpace(description) || description.Trim().Length > MaxDescriptionLength)
        {
            throw new DomainException($"Description is required and must have at most {MaxDescriptionLength} characters.");
        }

        return description.Trim();
    }

    private void Raise(AccountEvent @event)
    {
        Apply(@event);
        _pendingEvents.Add(@event);
    }

    private void Apply(AccountEvent @event)
    {
        switch (@event)
        {
            case AccountOpened e:
                Id = e.AccountId;
                Owner = e.Owner;
                Currency = e.Currency;
                Balance = Money.Zero(e.Currency);
                DailyWithdrawalLimit = Money.Positive(e.DailyWithdrawalLimit, e.Currency);
                _withdrawnOnThatDay = Money.Zero(e.Currency);
                break;
            case MoneyDeposited e:
                Balance = Balance.Add(Money.Positive(e.Amount, Currency));
                break;
            case TransferReceived e:
                Balance = Balance.Add(Money.Positive(e.Amount, Currency));
                break;
            case MoneyWithdrawn e:
                TakeOut(e.Amount, e.OccurredAt);
                break;
            case TransferSent e:
                TakeOut(e.Amount, e.OccurredAt);
                break;
            case AccountClosed:
                IsClosed = true;
                break;
            default:
                throw new InvalidOperationException($"Unknown event {@event.GetType().Name}.");
        }
    }

    private void TakeOut(decimal amount, DateTimeOffset occurredAt)
    {
        var money = Money.Positive(amount, Currency);
        Balance = Balance.Subtract(money);

        var day = DateOnly.FromDateTime(occurredAt.UtcDateTime);
        _withdrawnOnThatDay = day == _withdrawalDay ? _withdrawnOnThatDay.Add(money) : money;
        _withdrawalDay = day;
    }
}
