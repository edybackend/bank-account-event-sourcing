using System.Net.Http.Json;

namespace BankAccount.IntegrationTests;

public class SampleDataTests(SeededBankApiFactory factory) : IClassFixture<SeededBankApiFactory>
{
    [Fact]
    public async Task Every_read_model_matches_a_full_replay_of_its_stream()
    {
        var client = factory.CreateClient();
        var ct = TestContext.Current.CancellationToken;

        var accounts = await client.GetFromJsonAsync<List<AccountSummary>>("/api/accounts", ct);

        Assert.Equal(4, accounts!.Count);
        Assert.Contains(accounts, a => a.Status == "Closed");
        foreach (var account in accounts)
        {
            var statement = await client.GetFromJsonAsync<PagedResponse<StatementLine>>($"/api/accounts/{account.Id}/statement?pageSize=100", ct);
            Assert.Equal(account.Balance, (await client.ReplayAsync(account.Id)).Balance);
            Assert.Equal(account.Balance, statement!.Items.Sum(l => l.Amount));
        }
    }
}
