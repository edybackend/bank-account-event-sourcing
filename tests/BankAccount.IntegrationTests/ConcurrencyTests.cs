using System.Net;

namespace BankAccount.IntegrationTests;

/// <summary>Simultaneous commands on one stream: no event is lost and no rule is skipped.</summary>
public class ConcurrencyTests(BankApiFactory factory) : IClassFixture<BankApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task Concurrent_deposits_never_lose_an_event()
    {
        var id = await _client.OpenAsync();

        var responses = await Task.WhenAll(Enumerable.Range(1, 8).Select(i => _client.DepositAsync(id, 10m, $"Deposit {i}")));

        // Losers are retried on the fresh stream; if a request still gives up it says so with 409 and stores nothing.
        Assert.All(responses, r => Assert.Contains(r.StatusCode, new[] { HttpStatusCode.OK, HttpStatusCode.Conflict }));
        var accepted = responses.Count(r => r.StatusCode == HttpStatusCode.OK);
        var summary = await _client.SummaryAsync(id);
        Assert.Equal(10m * accepted, summary.Balance);
        Assert.Equal(1 + accepted, summary.Version);
        Assert.Equal(summary.Balance, (await _client.ReplayAsync(id)).Balance);
    }

    [Fact]
    public async Task Concurrent_withdrawals_can_never_overdraw()
    {
        var id = await _client.OpenAsync(deposit: 100m);

        var responses = await Task.WhenAll(Enumerable.Range(1, 10).Select(i => _client.WithdrawAsync(id, 30m, $"Withdrawal {i}")));

        var accepted = responses.Count(r => r.StatusCode == HttpStatusCode.OK);
        var summary = await _client.SummaryAsync(id);
        Assert.InRange(accepted, 1, 3); // 3 x 30 = 90 is the most that fits in 100
        Assert.Equal(100m - (30m * accepted), summary.Balance);
        Assert.True(summary.Balance >= 0);
        Assert.Equal(summary.Balance, (await _client.ReplayAsync(id)).Balance);
    }
}
