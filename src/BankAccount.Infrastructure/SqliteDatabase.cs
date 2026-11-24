using Microsoft.Data.Sqlite;

namespace BankAccount.Infrastructure;

/// <summary>Opens SQLite connections and owns the schema: the event table and the read-model tables.</summary>
public sealed class SqliteDatabase(string connectionString)
{
    // Money in the read model is stored in cents (INTEGER) so sums are exact.
    private const string Schema = """
        PRAGMA journal_mode = WAL;

        CREATE TABLE IF NOT EXISTS events (
            position     INTEGER PRIMARY KEY AUTOINCREMENT,
            stream_id    TEXT    NOT NULL,
            version      INTEGER NOT NULL,
            type         TEXT    NOT NULL,
            data         TEXT    NOT NULL,
            occurred_at  TEXT    NOT NULL,
            UNIQUE (stream_id, version)
        );

        CREATE TABLE IF NOT EXISTS account_summaries (
            account_id     TEXT    PRIMARY KEY,
            owner          TEXT    NOT NULL,
            currency       TEXT    NOT NULL,
            balance_cents  INTEGER NOT NULL,
            status         TEXT    NOT NULL,
            version        INTEGER NOT NULL,
            opened_at      TEXT    NOT NULL
        );

        CREATE TABLE IF NOT EXISTS statement_lines (
            position             INTEGER PRIMARY KEY,
            account_id           TEXT    NOT NULL,
            type                 TEXT    NOT NULL,
            amount_cents         INTEGER NOT NULL,
            balance_after_cents  INTEGER NOT NULL,
            description          TEXT    NOT NULL,
            counterparty_id      TEXT    NULL,
            occurred_at          TEXT    NOT NULL
        );

        CREATE INDEX IF NOT EXISTS ix_statement_lines_account ON statement_lines (account_id, position);
        """;

    public string DataSource => new SqliteConnectionStringBuilder(connectionString).DataSource;

    public async Task<SqliteConnection> OpenAsync(CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        // Writers wait for each other instead of failing immediately with "database is locked".
        await using var pragma = connection.CreateCommand();
        pragma.CommandText = "PRAGMA busy_timeout = 5000;";
        await pragma.ExecuteNonQueryAsync(cancellationToken);
        return connection;
    }

    public async Task CreateSchemaAsync(CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(DataSource));
        if (!string.IsNullOrEmpty(directory) && DataSource != ":memory:")
        {
            Directory.CreateDirectory(directory);
        }

        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = Schema;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
