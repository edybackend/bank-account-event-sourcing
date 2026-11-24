using BankAccount.Domain;

namespace BankAccount.Application.Abstractions;

/// <summary>Append-only store of account streams. Projections are updated in the same transaction as the append.</summary>
public interface IEventStore
{
    /// <summary>Reads a stream in order. With <paramref name="until"/>, only events that happened at or before it.</summary>
    Task<IReadOnlyList<AccountEvent>> ReadStreamAsync(Guid accountId, DateTimeOffset? until, CancellationToken cancellationToken);

    /// <summary>Appends to one or more streams atomically: all or nothing.</summary>
    /// <exception cref="ConcurrencyConflictException">A stream is no longer at the expected version.</exception>
    Task AppendAsync(IReadOnlyList<StreamAppend> appends, CancellationToken cancellationToken);
}

/// <param name="ExpectedVersion">Number of events the writer saw in the stream. 0 for a new stream.</param>
public sealed record StreamAppend(Guid AccountId, int ExpectedVersion, IReadOnlyList<AccountEvent> Events);

/// <summary>Another writer appended to the stream after it was read (optimistic concurrency).</summary>
public sealed class ConcurrencyConflictException(Guid accountId, Exception? innerException = null)
    : Exception($"Stream {accountId} was changed by another writer.", innerException)
{
    public Guid AccountId { get; } = accountId;
}
