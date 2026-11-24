using System.Net.Http.Json;

namespace BankAccount.IntegrationTests;

internal sealed record AccountChanged(Guid AccountId, decimal Balance, string Currency, int Version);

internal sealed record TransferCompleted(Guid TransferId, AccountChanged From, AccountChanged To);

internal sealed record AccountSummary(Guid Id, string Owner, string Currency, decimal Balance, string Status, int Version);

internal sealed record StatementLine(long Position, string Type, decimal Amount, decimal BalanceAfter, string Description, Guid? CounterpartyAccountId);

internal sealed record PagedResponse<T>(List<T> Items, int Page, int PageSize, int TotalCount);

internal sealed record BalanceAt(Guid AccountId, string Currency, decimal Balance, DateTimeOffset At, int EventsReplayed);

internal sealed record EventView(int Version, string Type, DateTimeOffset OccurredAt);

internal sealed record Problem(int Status, string? Detail, Dictionary<string, string[]>? Errors);

internal static class ApiClient
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public static async Task<Guid> OpenAsync(this HttpClient client, string currency = "BRL", decimal deposit = 0m)
    {
        var response = await client.PostAsJsonAsync("/api/accounts", new { owner = "Test owner", currency }, Ct);
        response.EnsureSuccessStatusCode();
        var id = (await response.Content.ReadFromJsonAsync<AccountChanged>(Ct))!.AccountId;
        if (deposit > 0)
        {
            (await client.DepositAsync(id, deposit)).EnsureSuccessStatusCode();
        }

        return id;
    }

    public static Task<HttpResponseMessage> DepositAsync(this HttpClient client, Guid id, decimal amount, string description = "Deposit") =>
        client.PostAsJsonAsync($"/api/accounts/{id}/deposits", new { amount, description }, Ct);

    public static Task<HttpResponseMessage> WithdrawAsync(this HttpClient client, Guid id, decimal amount, string description = "Withdrawal") =>
        client.PostAsJsonAsync($"/api/accounts/{id}/withdrawals", new { amount, description }, Ct);

    public static Task<HttpResponseMessage> TransferAsync(this HttpClient client, Guid from, Guid to, decimal amount) =>
        client.PostAsJsonAsync("/api/transfers", new { fromAccountId = from, toAccountId = to, amount, description = "Transfer" }, Ct);

    public static async Task<AccountSummary> SummaryAsync(this HttpClient client, Guid id) =>
        (await client.GetFromJsonAsync<AccountSummary>($"/api/accounts/{id}", Ct))!;

    public static async Task<BalanceAt> ReplayAsync(this HttpClient client, Guid id) =>
        (await client.GetFromJsonAsync<BalanceAt>($"/api/accounts/{id}/balance", Ct))!;
}
