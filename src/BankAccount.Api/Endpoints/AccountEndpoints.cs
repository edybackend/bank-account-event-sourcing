using BankAccount.Application.Commands;
using BankAccount.Application.Queries;
using BankAccount.Domain;

namespace BankAccount.Api.Endpoints;

internal static class AccountEndpoints
{
    public static IEndpointRouteBuilder MapAccountEndpoints(this IEndpointRouteBuilder app)
    {
        var accounts = app.MapGroup("/api/accounts").WithTags("Accounts");

        // Commands: each appends events to the account's stream.
        accounts.MapPost("/", async (OpenAccountRequest request, AccountCommandHandler handler, CancellationToken ct) =>
                (await handler.OpenAsync(new OpenAccountCommand(request.Owner, request.Currency), ct))
                .ToHttp(changed => TypedResults.CreatedAtRoute(changed, "GetAccount", new { id = changed.AccountId })))
            .WithName("OpenAccount")
            .WithSummary("Open an account (AccountOpened)");

        accounts.MapPost("/{id:guid}/deposits", async (Guid id, MoneyRequest request, AccountCommandHandler handler, CancellationToken ct) =>
                (await handler.DepositAsync(new DepositCommand(id, request.Amount, request.Description), ct)).ToHttp(TypedResults.Ok))
            .WithName("Deposit")
            .WithSummary("Deposit money (MoneyDeposited)");

        accounts.MapPost("/{id:guid}/withdrawals", async (Guid id, MoneyRequest request, AccountCommandHandler handler, CancellationToken ct) =>
                (await handler.WithdrawAsync(new WithdrawCommand(id, request.Amount, request.Description), ct)).ToHttp(TypedResults.Ok))
            .WithName("Withdraw")
            .WithSummary("Withdraw money (MoneyWithdrawn); no overdraft, daily limit");

        accounts.MapPost("/{id:guid}/close", async (Guid id, AccountCommandHandler handler, CancellationToken ct) =>
                (await handler.CloseAsync(new CloseAccountCommand(id), ct)).ToHttp(TypedResults.Ok))
            .WithName("CloseAccount")
            .WithSummary("Close an account with zero balance (AccountClosed)");

        // Queries: read models (projections) and the event stream itself.
        accounts.MapGet("/", async (IAccountReadModel readModel, CancellationToken ct) => TypedResults.Ok(await readModel.ListAsync(ct)))
            .WithName("ListAccounts")
            .WithSummary("List account summaries (read model)");

        accounts.MapGet("/{id:guid}", async (Guid id, IAccountReadModel readModel, CancellationToken ct) =>
                await readModel.GetAsync(id, ct) is { } summary ? Results.Ok(summary) : Results.NotFound())
            .WithName("GetAccount")
            .WithSummary("Get an account summary (read model)");

        accounts.MapGet("/{id:guid}/statement", async (Guid id, IAccountReadModel readModel, CancellationToken ct, int? page, int? pageSize) =>
                await readModel.GetAsync(id, ct) is null
                    ? Results.NotFound()
                    : Results.Ok(await readModel.GetStatementAsync(id, new PageRequest(page, pageSize), ct)))
            .WithName("GetStatement")
            .WithSummary("Statement lines, newest first (read model)");

        accounts.MapGet("/{id:guid}/balance", async (Guid id, DateTimeOffset? at, AccountHistory history, CancellationToken ct) =>
                (await history.GetBalanceAtAsync(id, at, ct)).ToHttp(TypedResults.Ok))
            .WithName("GetBalanceAt")
            .WithSummary("Balance at any moment, by replaying the stream up to 'at'");

        accounts.MapGet("/{id:guid}/events", async (Guid id, AccountHistory history, CancellationToken ct) =>
                await history.GetEventsAsync(id, ct) is { } events
                    ? Results.Ok(events.Select((e, index) => new StoredEventView(index + 1, e.GetType().Name, e.OccurredAt, e)))
                    : Results.NotFound())
            .WithName("GetEvents")
            .WithSummary("The raw event stream of the account");

        app.MapPost("/api/transfers", async (TransferRequest request, AccountCommandHandler handler, CancellationToken ct) =>
                (await handler.TransferAsync(new TransferCommand(request.FromAccountId, request.ToAccountId, request.Amount, request.Description), ct))
                .ToHttp(TypedResults.Ok))
            .WithTags("Transfers")
            .WithName("Transfer")
            .WithSummary("Transfer between accounts (TransferSent + TransferReceived, one transaction)");

        return app;
    }
}

internal sealed record OpenAccountRequest(string Owner, string Currency);

internal sealed record MoneyRequest(decimal Amount, string Description);

internal sealed record TransferRequest(Guid FromAccountId, Guid ToAccountId, decimal Amount, string Description);

/// <summary>An event as shown by the API: its position in the stream, its type and its data.</summary>
internal sealed record StoredEventView(int Version, string Type, DateTimeOffset OccurredAt, AccountEvent Data);
