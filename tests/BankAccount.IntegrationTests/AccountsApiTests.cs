using System.Net;
using System.Net.Http.Json;

namespace BankAccount.IntegrationTests;

public class AccountsApiTests(BankApiFactory factory) : IClassFixture<BankApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Open_returns_201_and_the_summary_is_readable_at_the_location()
    {
        var response = await _client.PostAsJsonAsync("/api/accounts", new { owner = "Ana", currency = "eur" }, Ct);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var summary = await _client.GetFromJsonAsync<AccountSummary>(response.Headers.Location, Ct);
        Assert.Equal("EUR", summary!.Currency);
        Assert.Equal("Open", summary.Status);
        Assert.Equal(0m, summary.Balance);
        Assert.Equal(1, summary.Version);
    }

    [Fact]
    public async Task Each_command_appends_one_event_and_the_projections_follow()
    {
        var id = await _client.OpenAsync(deposit: 500m);

        (await _client.WithdrawAsync(id, 120.40m, "ATM")).EnsureSuccessStatusCode();

        var events = (await _client.GetFromJsonAsync<List<EventView>>($"/api/accounts/{id}/events", Ct))!;
        Assert.Equal(["AccountOpened", "MoneyDeposited", "MoneyWithdrawn"], events.Select(e => e.Type));
        Assert.Equal([1, 2, 3], events.Select(e => e.Version));

        var summary = await _client.SummaryAsync(id);
        var statement = await _client.GetFromJsonAsync<PagedResponse<StatementLine>>($"/api/accounts/{id}/statement", Ct);
        Assert.Equal(379.60m, summary.Balance);
        Assert.Equal(3, summary.Version);
        Assert.Equal(["Withdrawal", "Deposit"], statement!.Items.Select(l => l.Type));
        Assert.Equal(summary.Balance, statement.Items.Sum(l => l.Amount));
        Assert.Equal(summary.Balance, statement.Items[0].BalanceAfter);
        Assert.Equal(summary.Balance, (await _client.ReplayAsync(id)).Balance);
    }

    [Fact]
    public async Task Invalid_input_is_400_with_every_field()
    {
        var id = await _client.OpenAsync();

        var response = await _client.PostAsJsonAsync($"/api/accounts/{id}/deposits", new { amount = 1.234m, description = "" }, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<Problem>(Ct);
        Assert.Equal(["amount", "description"], problem!.Errors!.Keys.Order());
    }

    [Fact]
    public async Task Overdraft_is_422_and_appends_nothing()
    {
        var id = await _client.OpenAsync(deposit: 50m);

        var response = await _client.WithdrawAsync(id, 50.01m);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal(2, (await _client.SummaryAsync(id)).Version);
    }

    [Fact]
    public async Task Closing_requires_zero_balance_and_then_blocks_new_money()
    {
        var id = await _client.OpenAsync(deposit: 10m);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await _client.PostAsync($"/api/accounts/{id}/close", null, Ct)).StatusCode);
        (await _client.WithdrawAsync(id, 10m)).EnsureSuccessStatusCode();
        (await _client.PostAsync($"/api/accounts/{id}/close", null, Ct)).EnsureSuccessStatusCode();

        Assert.Equal("Closed", (await _client.SummaryAsync(id)).Status);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await _client.DepositAsync(id, 1m)).StatusCode);
    }

    [Fact]
    public async Task Transfer_moves_money_atomically_and_keeps_the_total()
    {
        var from = await _client.OpenAsync(deposit: 300m);
        var to = await _client.OpenAsync(deposit: 20m);

        var response = await _client.TransferAsync(from, to, 120m);

        var result = await response.Content.ReadFromJsonAsync<TransferCompleted>(Ct);
        Assert.Equal(180m, result!.From.Balance);
        Assert.Equal(140m, result.To.Balance);
        Assert.Equal(320m, (await _client.SummaryAsync(from)).Balance + (await _client.SummaryAsync(to)).Balance);
        var incoming = await _client.GetFromJsonAsync<PagedResponse<StatementLine>>($"/api/accounts/{to}/statement", Ct);
        Assert.Equal(from, incoming!.Items[0].CounterpartyAccountId);
    }

    [Fact]
    public async Task Rejected_transfer_changes_neither_account()
    {
        var from = await _client.OpenAsync(deposit: 300m);
        var to = await _client.OpenAsync("USD");

        var response = await _client.TransferAsync(from, to, 10m);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal(2, (await _client.SummaryAsync(from)).Version);
        Assert.Equal(1, (await _client.SummaryAsync(to)).Version);
    }

    [Fact]
    public async Task Balance_before_the_account_existed_is_404()
    {
        var id = await _client.OpenAsync();

        var response = await _client.GetAsync($"/api/accounts/{id}/balance?at=2020-01-01T00:00:00Z", Ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Unknown_account_and_malformed_json_are_404_and_400()
    {
        var unknown = Guid.NewGuid();
        using var badJson = new StringContent("{ oops", System.Text.Encoding.UTF8, "application/json");

        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync($"/api/accounts/{unknown}", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync($"/api/accounts/{unknown}/events", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.DepositAsync(unknown, 1m)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.PostAsync("/api/transfers", badJson, Ct)).StatusCode);
    }
}
