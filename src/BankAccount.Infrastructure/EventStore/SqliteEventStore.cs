using System.Globalization;
using BankAccount.Application.Abstractions;
using BankAccount.Domain;
using BankAccount.Infrastructure.ReadModel;
using Dapper;
using Microsoft.Data.Sqlite;

namespace BankAccount.Infrastructure.EventStore;

internal sealed class SqliteEventStore(SqliteDatabase database) : IEventStore
{
    private const int SqliteConstraintUnique = 2067;

    public async Task<IReadOnlyList<AccountEvent>> ReadStreamAsync(Guid accountId, DateTimeOffset? until, CancellationToken cancellationToken)
    {
        await using var connection = await database.OpenAsync(cancellationToken);
        var rows = await connection.QueryAsync<(string Type, string Data)>(
            new CommandDefinition(
                """
                SELECT type, data FROM events
                WHERE stream_id = @StreamId AND (@Until IS NULL OR occurred_at <= @Until)
                ORDER BY version
                """,
                new { StreamId = accountId.ToString(), Until = until is { } u ? Timestamp(u) : null },
                cancellationToken: cancellationToken));

        return rows.Select(row => EventSerializer.Deserialize(row.Type, row.Data)).ToList();
    }

    public async Task AppendAsync(IReadOnlyList<StreamAppend> appends, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(appends);

        await using var connection = await database.OpenAsync(cancellationToken);

        // BEGIN IMMEDIATE takes the write lock up front, so "check version, then insert" cannot interleave with another writer.
        await using var transaction = connection.BeginTransaction(deferred: false);

        foreach (var append in appends)
        {
            var current = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
                "SELECT COALESCE(MAX(version), 0) FROM events WHERE stream_id = @StreamId",
                new { StreamId = append.AccountId.ToString() },
                transaction,
                cancellationToken: cancellationToken));

            if (current != append.ExpectedVersion)
            {
                throw new ConcurrencyConflictException(append.AccountId);
            }

            var version = append.ExpectedVersion;
            foreach (var @event in append.Events)
            {
                version++;
                long position;
                try
                {
                    position = await connection.ExecuteScalarAsync<long>(new CommandDefinition(
                        """
                        INSERT INTO events (stream_id, version, type, data, occurred_at)
                        VALUES (@StreamId, @Version, @Type, @Data, @OccurredAt)
                        RETURNING position
                        """,
                        new
                        {
                            StreamId = append.AccountId.ToString(),
                            Version = version,
                            Type = EventSerializer.TypeName(@event),
                            Data = EventSerializer.Serialize(@event),
                            OccurredAt = Timestamp(@event.OccurredAt),
                        },
                        transaction,
                        cancellationToken: cancellationToken));
                }
                catch (SqliteException ex) when (ex.SqliteExtendedErrorCode == SqliteConstraintUnique)
                {
                    // Second guard: the unique (stream_id, version) index.
                    throw new ConcurrencyConflictException(append.AccountId, ex);
                }

                // Read models are updated in the same transaction: a query right after a command sees its effect.
                await AccountProjection.ApplyAsync(connection, transaction, position, version, @event, cancellationToken);
            }
        }

        await transaction.CommitAsync(cancellationToken);
    }

    /// <summary>UTC, fixed-width ISO 8601, so text comparison in SQL is chronological.</summary>
    internal static string Timestamp(DateTimeOffset value) =>
        value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'", CultureInfo.InvariantCulture);
}
