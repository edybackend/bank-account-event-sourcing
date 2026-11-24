using BankAccount.Domain;

namespace BankAccount.UnitTests;

/// <summary>Given past events, when a command runs, then these new events are raised (or the command is rejected).</summary>
public class AccountTests
{
    private static readonly Guid Id = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid OtherId = Guid.Parse("00000000-0000-0000-0000-000000000002");
    private static readonly DateTimeOffset Day1 = new(2026, 3, 10, 9, 0, 0, TimeSpan.Zero);

    private static AccountOpened Opened(Guid? id = null, string currency = "BRL") =>
        new(id ?? Id, "Ana Souza", currency, 5_000m, Day1);

    private static Account Given(params AccountEvent[] history) => Account.FromHistory(history);

    [Fact]
    public void Open_raises_AccountOpened_with_zero_balance_and_default_limit()
    {
        var account = Account.Open(Id, " Ana Souza ", "brl", Day1);

        var opened = Assert.IsType<AccountOpened>(Assert.Single(account.PendingEvents));
        Assert.Equal(new AccountOpened(Id, "Ana Souza", "BRL", 5_000m, Day1), opened);
        Assert.Equal(0m, account.Balance.Amount);
        Assert.Equal(0, account.Version);
    }

    [Theory]
    [InlineData("", "BRL")]
    [InlineData("Ana", "GBP")]
    public void Open_rejects_invalid_owner_or_currency(string owner, string currency) =>
        Assert.Throws<DomainException>(() => Account.Open(Id, owner, currency, Day1));

    [Fact]
    public void Replaying_the_history_rebuilds_balance_and_version()
    {
        var account = Given(
            Opened(),
            new MoneyDeposited(Id, 1_000m, "Salary", Day1),
            new MoneyWithdrawn(Id, 250.50m, "ATM", Day1),
            new TransferReceived(Id, Guid.NewGuid(), OtherId, 100m, "Rent split", Day1));

        Assert.Equal(849.50m, account.Balance.Amount);
        Assert.Equal(4, account.Version);
        Assert.Empty(account.PendingEvents);
    }

    [Fact]
    public void Deposit_raises_MoneyDeposited()
    {
        var account = Given(Opened());

        account.Deposit(10.25m, "Cash", Day1);

        Assert.Equal(new MoneyDeposited(Id, 10.25m, "Cash", Day1), Assert.Single(account.PendingEvents));
        Assert.Equal(10.25m, account.Balance.Amount);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(1.001)]
    public void Amount_must_be_positive_with_two_decimals(double amount) =>
        Assert.Throws<DomainException>(() => Given(Opened()).Deposit((decimal)amount, "Cash", Day1));

    [Fact]
    public void Withdrawal_above_balance_is_rejected()
    {
        var account = Given(Opened(), new MoneyDeposited(Id, 100m, "Cash", Day1));

        var error = Assert.Throws<DomainException>(() => account.Withdraw(100.01m, "ATM", Day1));

        Assert.Contains("Insufficient funds", error.Message, StringComparison.Ordinal);
        Assert.Empty(account.PendingEvents);
    }

    [Fact]
    public void Daily_limit_counts_withdrawals_and_transfers_of_the_same_day()
    {
        var account = Given(
            Opened(),
            new MoneyDeposited(Id, 20_000m, "Bonus", Day1),
            new MoneyWithdrawn(Id, 3_000m, "ATM", Day1),
            new TransferSent(Id, Guid.NewGuid(), OtherId, 1_500m, "Rent", Day1.AddHours(2)));

        Assert.Throws<DomainException>(() => account.Withdraw(500.01m, "ATM", Day1.AddHours(5)));
        account.Withdraw(500m, "ATM", Day1.AddHours(5)); // exactly the limit
    }

    [Fact]
    public void Daily_limit_starts_again_on_the_next_day()
    {
        var account = Given(
            Opened(),
            new MoneyDeposited(Id, 20_000m, "Bonus", Day1),
            new MoneyWithdrawn(Id, 5_000m, "ATM", Day1));

        account.Withdraw(5_000m, "ATM", Day1.AddDays(1));

        Assert.Equal(10_000m, account.Balance.Amount);
    }

    [Fact]
    public void Transfer_raises_one_event_on_each_account_with_the_same_transfer_id()
    {
        var source = Given(Opened(), new MoneyDeposited(Id, 300m, "Cash", Day1));
        var destination = Given(Opened(OtherId));
        var transferId = Guid.NewGuid();

        source.SendTransfer(transferId, destination, 120m, "Dinner", Day1);

        Assert.Equal(new TransferSent(Id, transferId, OtherId, 120m, "Dinner", Day1), Assert.Single(source.PendingEvents));
        Assert.Equal(new TransferReceived(OtherId, transferId, Id, 120m, "Dinner", Day1), Assert.Single(destination.PendingEvents));
        Assert.Equal(180m, source.Balance.Amount);
        Assert.Equal(120m, destination.Balance.Amount);
    }

    [Fact]
    public void Transfer_between_currencies_is_rejected_and_nothing_is_raised()
    {
        var source = Given(Opened(), new MoneyDeposited(Id, 300m, "Cash", Day1));
        var destination = Given(Opened(OtherId, "USD"));

        Assert.Throws<DomainException>(() => source.SendTransfer(Guid.NewGuid(), destination, 10m, "Dinner", Day1));

        Assert.Empty(source.PendingEvents);
        Assert.Empty(destination.PendingEvents);
    }

    [Fact]
    public void Transfer_to_a_closed_account_is_rejected_and_nothing_is_raised()
    {
        var source = Given(Opened(), new MoneyDeposited(Id, 300m, "Cash", Day1));
        var destination = Given(Opened(OtherId), new AccountClosed(OtherId, Day1));

        Assert.Throws<DomainException>(() => source.SendTransfer(Guid.NewGuid(), destination, 10m, "Dinner", Day1));

        Assert.Empty(source.PendingEvents);
    }

    [Fact]
    public void Transfer_to_the_same_account_is_rejected()
    {
        var account = Given(Opened(), new MoneyDeposited(Id, 300m, "Cash", Day1));

        Assert.Throws<DomainException>(() => account.SendTransfer(Guid.NewGuid(), account, 10m, "Self", Day1));
    }

    [Fact]
    public void Account_with_balance_cannot_be_closed()
    {
        var account = Given(Opened(), new MoneyDeposited(Id, 1m, "Cash", Day1));

        Assert.Throws<DomainException>(() => account.Close(Day1));
    }

    [Fact]
    public void Closed_account_accepts_no_more_money()
    {
        var account = Given(Opened(), new AccountClosed(Id, Day1));

        Assert.Throws<DomainException>(() => account.Deposit(1m, "Cash", Day1));
        Assert.Throws<DomainException>(() => account.Close(Day1));
    }

    [Fact]
    public void Money_never_mixes_currencies() =>
        Assert.Throws<DomainException>(() => Money.Zero("BRL").Add(Money.Positive(1m, "USD")));
}
