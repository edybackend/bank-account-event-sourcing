using BankAccount.Application;
using BankAccount.Application.Abstractions;
using BankAccount.Application.Queries;
using BankAccount.Domain;

namespace BankAccount.UnitTests;

public class AccountHistoryTests
{
    private static readonly Guid Id = Guid.NewGuid();
    private static readonly DateTimeOffset March1 = new(2026, 3, 1, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Balance_at_a_past_moment_replays_only_the_events_until_then()
    {
        var store = new InMemoryEventStore();
        await store.AppendAsync(
            [new StreamAppend(Id, 0,
            [
                new AccountOpened(Id, "Ana", "BRL", 5_000m, March1),
                new MoneyDeposited(Id, 1_000m, "Salary", March1.AddDays(4)),
                new MoneyWithdrawn(Id, 300m, "Rent", March1.AddDays(9)),
            ])],
            TestContext.Current.CancellationToken);
        var history = new AccountHistory(store, TimeProvider.System);

        var midMonth = await history.GetBalanceAtAsync(Id, March1.AddDays(5), TestContext.Current.CancellationToken);
        var beforeOpening = await history.GetBalanceAtAsync(Id, March1.AddDays(-1), TestContext.Current.CancellationToken);
        var now = await history.GetBalanceAtAsync(Id, null, TestContext.Current.CancellationToken);

        Assert.Equal(1_000m, midMonth.Value!.Balance);
        Assert.Equal(2, midMonth.Value.EventsReplayed);
        Assert.Equal(FailureKind.NotFound, beforeOpening.Failure!.Kind);
        Assert.Equal(700m, now.Value!.Balance);
    }
}
