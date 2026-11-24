using System.Globalization;
using BankAccount.Application.Queries;
using Dapper;

namespace BankAccount.Infrastructure.ReadModel;

internal sealed class SqliteAccountReadModel(SqliteDatabase database) : IAccountReadModel
{
    private const string SummaryColumns = "account_id AS AccountId, owner AS Owner, currency AS Currency, balance_cents AS BalanceCents, status AS Status, version AS Version, opened_at AS OpenedAt";

    public async Task<IReadOnlyList<AccountSummary>> ListAsync(CancellationToken cancellationToken)
    {
        await using var connection = await database.OpenAsync(cancellationToken);
        var rows = await connection.QueryAsync<SummaryRow>(new CommandDefinition(
            $"SELECT {SummaryColumns} FROM account_summaries ORDER BY opened_at", cancellationToken: cancellationToken));
        return rows.Select(ToSummary).ToList();
    }

    public async Task<AccountSummary?> GetAsync(Guid accountId, CancellationToken cancellationToken)
    {
        await using var connection = await database.OpenAsync(cancellationToken);
        var row = await connection.QuerySingleOrDefaultAsync<SummaryRow>(new CommandDefinition(
            $"SELECT {SummaryColumns} FROM account_summaries WHERE account_id = @Id",
            new { Id = accountId.ToString() },
            cancellationToken: cancellationToken));
        return row is null ? null : ToSummary(row);
    }

    public async Task<PagedResult<StatementLine>> GetStatementAsync(Guid accountId, PageRequest page, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(page);

        await using var connection = await database.OpenAsync(cancellationToken);
        var parameters = new { Id = accountId.ToString(), page.PageSize, page.Skip };
        var total = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT COUNT(*) FROM statement_lines WHERE account_id = @Id", parameters, cancellationToken: cancellationToken));
        var rows = await connection.QueryAsync<StatementRow>(new CommandDefinition(
            """
            SELECT position AS Position, type AS Type, amount_cents AS AmountCents, balance_after_cents AS BalanceAfterCents,
                   description AS Description, counterparty_id AS CounterpartyId, occurred_at AS OccurredAt
            FROM statement_lines WHERE account_id = @Id
            ORDER BY position DESC
            LIMIT @PageSize OFFSET @Skip
            """,
            parameters,
            cancellationToken: cancellationToken));

        var items = rows.Select(row => new StatementLine(
            row.Position,
            row.Type,
            row.AmountCents / 100m,
            row.BalanceAfterCents / 100m,
            row.Description,
            row.CounterpartyId is null ? null : Guid.Parse(row.CounterpartyId),
            Parse(row.OccurredAt))).ToList();

        return new PagedResult<StatementLine>(items, page.Page, page.PageSize, total);
    }

    private static AccountSummary ToSummary(SummaryRow row) =>
        new(Guid.Parse(row.AccountId), row.Owner, row.Currency, row.BalanceCents / 100m, row.Status, (int)row.Version, Parse(row.OpenedAt));

    private static DateTimeOffset Parse(string timestamp) =>
        DateTimeOffset.Parse(timestamp, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal);

    // Dapper materializes these by property name; SQLite returns INTEGER as long and TEXT as string.
    private sealed class SummaryRow
    {
        public string AccountId { get; init; } = string.Empty;

        public string Owner { get; init; } = string.Empty;

        public string Currency { get; init; } = string.Empty;

        public long BalanceCents { get; init; }

        public string Status { get; init; } = string.Empty;

        public long Version { get; init; }

        public string OpenedAt { get; init; } = string.Empty;
    }

    private sealed class StatementRow
    {
        public long Position { get; init; }

        public string Type { get; init; } = string.Empty;

        public long AmountCents { get; init; }

        public long BalanceAfterCents { get; init; }

        public string Description { get; init; } = string.Empty;

        public string? CounterpartyId { get; init; }

        public string OccurredAt { get; init; } = string.Empty;
    }
}
