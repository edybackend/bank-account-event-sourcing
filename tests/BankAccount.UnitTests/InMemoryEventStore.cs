using BankAccount.Application.Abstractions;
using BankAccount.Domain;

namespace BankAccount.UnitTests;

/// <summary>Event store with the same version check as the real one, plus a hook to simulate another writer.</summary>
internal sealed class InMemoryEventStore : IEventStore
{
    private readonly Dictionary<Guid, List<AccountEvent>> _streams = [];

    /// <summary>Runs once before each append; lets a test append "from another request" at the worst moment.</summary>
    public Func<Task>? BeforeAppend { get; set; }

    public int AppendCalls { get; private set; }

    public IReadOnlyList<AccountEvent> Stream(Guid id) => _streams.TryGetValue(id, out var s) ? s : [];

    public Task<IReadOnlyList<AccountEvent>> ReadStreamAsync(Guid accountId, DateTimeOffset? until, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<AccountEvent>>(
            Stream(accountId).Where(e => until is null || e.OccurredAt <= until).ToList());

    public async Task AppendAsync(IReadOnlyList<StreamAppend> appends, CancellationToken cancellationToken)
    {
        AppendCalls++;
        if (BeforeAppend is { } hook)
        {
            BeforeAppend = null;
            await hook();
        }

        foreach (var append in appends)
        {
            if (Stream(append.AccountId).Count != append.ExpectedVersion)
            {
                throw new ConcurrencyConflictException(append.AccountId);
            }
        }

        foreach (var append in appends)
        {
            if (!_streams.TryGetValue(append.AccountId, out var stream))
            {
                _streams[append.AccountId] = stream = [];
            }

            stream.AddRange(append.Events);
        }
    }
}

internal sealed class FixedClock(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
}
