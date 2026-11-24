using BankAccount.Application;
using BankAccount.Application.Abstractions;
using BankAccount.Application.Commands;
using BankAccount.Domain;
using Microsoft.Extensions.Logging.Abstractions;

namespace BankAccount.UnitTests;

public class AccountCommandHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 3, 10, 12, 0, 0, TimeSpan.Zero);
    private readonly InMemoryEventStore _store = new();
    private readonly AccountCommandHandler _handler;

    public AccountCommandHandlerTests() =>
        _handler = new AccountCommandHandler(_store, new FixedClock(Now), NullLogger<AccountCommandHandler>.Instance);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<Guid> OpenWithAsync(decimal balance, string currency = "BRL")
    {
        var opened = await _handler.OpenAsync(new OpenAccountCommand("Ana", currency), Ct);
        if (balance > 0)
        {
            await _handler.DepositAsync(new DepositCommand(opened.Value!.AccountId, balance, "Initial"), Ct);
        }

        return opened.Value!.AccountId;
    }

    [Fact]
    public async Task Open_stores_the_first_event_of_a_new_stream()
    {
        var result = await _handler.OpenAsync(new OpenAccountCommand("Ana", "usd"), Ct);

        Assert.Equal("USD", result.Value!.Currency);
        Assert.Equal(1, result.Value.Version);
        Assert.IsType<AccountOpened>(Assert.Single(_store.Stream(result.Value.AccountId)));
    }

    [Fact]
    public async Task Invalid_input_lists_every_field_and_stores_nothing()
    {
        var result = await _handler.OpenAsync(new OpenAccountCommand("", "GBP"), Ct);

        Assert.Equal(FailureKind.Validation, result.Failure!.Kind);
        Assert.Equal(["currency", "owner"], result.Failure.Fields!.Keys.Order());
        Assert.Equal(0, _store.AppendCalls);
    }

    [Fact]
    public async Task Business_rule_breaks_are_422_and_store_nothing()
    {
        var id = await OpenWithAsync(50m);

        var result = await _handler.WithdrawAsync(new WithdrawCommand(id, 80m, "ATM"), Ct);

        Assert.Equal(FailureKind.BusinessRule, result.Failure!.Kind);
        Assert.Equal(2, _store.Stream(id).Count);
    }

    [Fact]
    public async Task Unknown_account_is_not_found()
    {
        var result = await _handler.DepositAsync(new DepositCommand(Guid.NewGuid(), 1m, "Cash"), Ct);

        Assert.Equal(FailureKind.NotFound, result.Failure!.Kind);
    }

    [Fact]
    public async Task A_command_that_loses_the_race_runs_again_on_the_fresh_stream()
    {
        var id = await OpenWithAsync(100m);

        // Another request withdraws 70 between our read and our append.
        _store.BeforeAppend = () => _store.AppendAsync(
            [new StreamAppend(id, 2, [new MoneyWithdrawn(id, 70m, "Other request", Now)])], Ct);

        var result = await _handler.WithdrawAsync(new WithdrawCommand(id, 50m, "ATM"), Ct);

        // Re-run on the real balance (30): the withdrawal of 50 is now refused instead of overdrawing.
        Assert.Equal(FailureKind.BusinessRule, result.Failure!.Kind);
        Assert.Contains("Insufficient funds", result.Failure.Message, StringComparison.Ordinal);
        Assert.Equal(30m, Account.FromHistory(_store.Stream(id)).Balance.Amount);
    }

    [Fact]
    public async Task A_retried_command_succeeds_when_the_fresh_state_allows_it()
    {
        var id = await OpenWithAsync(100m);
        _store.BeforeAppend = () => _store.AppendAsync(
            [new StreamAppend(id, 2, [new MoneyDeposited(id, 5m, "Other request", Now)])], Ct);

        var result = await _handler.WithdrawAsync(new WithdrawCommand(id, 50m, "ATM"), Ct);

        Assert.Equal(55m, result.Value!.Balance);
        Assert.Equal(4, result.Value.Version);
    }

    [Fact]
    public async Task Transfer_appends_both_streams_with_one_transfer_id()
    {
        var from = await OpenWithAsync(300m);
        var to = await OpenWithAsync(0m);

        var result = await _handler.TransferAsync(new TransferCommand(from, to, 120m, "Dinner"), Ct);

        Assert.Equal(180m, result.Value!.From.Balance);
        Assert.Equal(120m, result.Value.To.Balance);
        var sent = Assert.IsType<TransferSent>(_store.Stream(from)[^1]);
        var received = Assert.IsType<TransferReceived>(_store.Stream(to)[^1]);
        Assert.Equal(sent.TransferId, received.TransferId);
    }

    [Fact]
    public async Task Transfer_to_itself_is_a_validation_failure()
    {
        var id = await OpenWithAsync(10m);

        var result = await _handler.TransferAsync(new TransferCommand(id, id, 1m, "Self"), Ct);

        Assert.Equal("toAccountId", Assert.Single(result.Failure!.Fields!.Keys));
    }

    [Fact]
    public async Task Transfer_between_currencies_breaks_a_business_rule()
    {
        var from = await OpenWithAsync(10m);
        var to = await OpenWithAsync(0m, "EUR");

        var result = await _handler.TransferAsync(new TransferCommand(from, to, 1m, "FX"), Ct);

        Assert.Equal(FailureKind.BusinessRule, result.Failure!.Kind);
    }
}
