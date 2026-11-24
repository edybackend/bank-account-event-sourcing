using BankAccount.Application.Abstractions;
using BankAccount.Application.Commands;
using Microsoft.Extensions.Logging.Abstractions;

namespace BankAccount.Infrastructure;

/// <summary>
/// Sample accounts with about six weeks of history, created through the real command handler with a clock set in the past,
/// so they obey every rule and the balance-at-date query has something to show.
/// </summary>
internal static class SampleData
{
    public static async Task SeedAsync(IEventStore store, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var clock = new MovableClock();
        var handler = new AccountCommandHandler(store, clock, NullLogger<AccountCommandHandler>.Instance);
        var day0 = new DateTimeOffset(now.UtcDateTime.Date, TimeSpan.Zero).AddHours(10);

        async Task<Guid> Open(int daysAgo, string owner, string currency)
        {
            clock.Now = day0.AddDays(-daysAgo);
            return (await handler.OpenAsync(new OpenAccountCommand(owner, currency), cancellationToken)).Value!.AccountId;
        }

        async Task Run(int daysAgo, Func<Task> action)
        {
            clock.Now = day0.AddDays(-daysAgo);
            await action();
        }

        var ana = await Open(40, "Ana Souza", "BRL");
        var bruno = await Open(40, "Bruno Lima", "BRL");
        var carla = await Open(39, "Carla Mendes", "USD");
        var diego = await Open(38, "Diego Rocha", "BRL");

        await Run(39, () => handler.DepositAsync(new DepositCommand(ana, 8_500m, "Salary"), cancellationToken));
        await Run(38, () => handler.DepositAsync(new DepositCommand(bruno, 4_200m, "Salary"), cancellationToken));
        await Run(37, () => handler.DepositAsync(new DepositCommand(diego, 100m, "Opening deposit"), cancellationToken));
        await Run(36, () => handler.DepositAsync(new DepositCommand(carla, 3_000m, "Payroll"), cancellationToken));
        await Run(35, () => handler.WithdrawAsync(new WithdrawCommand(ana, 400m, "ATM"), cancellationToken));
        await Run(33, () => handler.TransferAsync(new TransferCommand(ana, bruno, 1_250m, "Rent share"), cancellationToken));
        await Run(30, () => handler.WithdrawAsync(new WithdrawCommand(bruno, 300m, "Groceries"), cancellationToken));
        await Run(25, () => handler.WithdrawAsync(new WithdrawCommand(carla, 120.50m, "Card payment"), cancellationToken));
        await Run(20, () => handler.DepositAsync(new DepositCommand(ana, 350.75m, "Refund"), cancellationToken));
        await Run(16, () => handler.WithdrawAsync(new WithdrawCommand(diego, 100m, "Moving to another bank"), cancellationToken));
        await Run(15, () => handler.CloseAsync(new CloseAccountCommand(diego), cancellationToken));
        await Run(10, () => handler.DepositAsync(new DepositCommand(ana, 8_500m, "Salary"), cancellationToken));
        await Run(9, () => handler.DepositAsync(new DepositCommand(bruno, 4_200m, "Salary"), cancellationToken));
        await Run(8, () => handler.TransferAsync(new TransferCommand(ana, bruno, 1_250m, "Rent share"), cancellationToken));
        await Run(3, () => handler.WithdrawAsync(new WithdrawCommand(ana, 980.40m, "Insurance"), cancellationToken));
    }

    private sealed class MovableClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; }

        public override DateTimeOffset GetUtcNow() => Now;
    }
}
