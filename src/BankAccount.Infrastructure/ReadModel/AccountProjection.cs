using BankAccount.Domain;
using BankAccount.Infrastructure.EventStore;
using Dapper;
using Microsoft.Data.Sqlite;

namespace BankAccount.Infrastructure.ReadModel;

/// <summary>Turns each stored event into rows of the two read models: the account summary and the statement.</summary>
internal static class AccountProjection
{
    public static Task ApplyAsync(
        SqliteConnection connection, SqliteTransaction transaction, long position, int version, AccountEvent @event, CancellationToken cancellationToken) =>
        @event switch
        {
            AccountOpened e => ExecuteAsync(
                connection,
                transaction,
                """
                INSERT INTO account_summaries (account_id, owner, currency, balance_cents, status, version, opened_at)
                VALUES (@Id, @Owner, @Currency, 0, 'Open', @Version, @OpenedAt)
                """,
                new { Id = e.AccountId.ToString(), e.Owner, e.Currency, Version = version, OpenedAt = SqliteEventStore.Timestamp(e.OccurredAt) },
                cancellationToken),
            MoneyDeposited e => MoveAsync(connection, transaction, position, version, e, "Deposit", e.Amount, e.Description, null, cancellationToken),
            MoneyWithdrawn e => MoveAsync(connection, transaction, position, version, e, "Withdrawal", -e.Amount, e.Description, null, cancellationToken),
            TransferSent e => MoveAsync(connection, transaction, position, version, e, "TransferOut", -e.Amount, e.Description, e.ToAccountId, cancellationToken),
            TransferReceived e => MoveAsync(connection, transaction, position, version, e, "TransferIn", e.Amount, e.Description, e.FromAccountId, cancellationToken),
            AccountClosed e => ExecuteAsync(
                connection,
                transaction,
                "UPDATE account_summaries SET status = 'Closed', version = @Version WHERE account_id = @Id",
                new { Id = e.AccountId.ToString(), Version = version },
                cancellationToken),
            _ => throw new InvalidOperationException($"No projection for {@event.GetType().Name}."),
        };

    private static async Task MoveAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        long position,
        int version,
        AccountEvent @event,
        string type,
        decimal amount,
        string description,
        Guid? counterparty,
        CancellationToken cancellationToken)
    {
        var cents = ToCents(amount);
        var balanceAfter = await connection.ExecuteScalarAsync<long>(new CommandDefinition(
            """
            UPDATE account_summaries SET balance_cents = balance_cents + @Cents, version = @Version
            WHERE account_id = @Id
            RETURNING balance_cents
            """,
            new { Id = @event.AccountId.ToString(), Cents = cents, Version = version },
            transaction,
            cancellationToken: cancellationToken));

        await ExecuteAsync(
            connection,
            transaction,
            """
            INSERT INTO statement_lines (position, account_id, type, amount_cents, balance_after_cents, description, counterparty_id, occurred_at)
            VALUES (@Position, @Id, @Type, @Cents, @BalanceAfter, @Description, @Counterparty, @OccurredAt)
            """,
            new
            {
                Position = position,
                Id = @event.AccountId.ToString(),
                Type = type,
                Cents = cents,
                BalanceAfter = balanceAfter,
                Description = description,
                Counterparty = counterparty?.ToString(),
                OccurredAt = SqliteEventStore.Timestamp(@event.OccurredAt),
            },
            cancellationToken);
    }

    private static Task<int> ExecuteAsync(SqliteConnection connection, SqliteTransaction transaction, string sql, object parameters, CancellationToken cancellationToken) =>
        connection.ExecuteAsync(new CommandDefinition(sql, parameters, transaction, cancellationToken: cancellationToken));

    internal static long ToCents(decimal amount) => (long)decimal.Round(amount * 100m, 0, MidpointRounding.ToEven);
}
