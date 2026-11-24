using BankAccount.Api.Endpoints;
using BankAccount.Application;
using BankAccount.Infrastructure;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddBankApplication();
builder.Services.AddBankInfrastructure(builder.Configuration.GetConnectionString("Bank") ?? "Data Source=data/bank.db");
builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();
builder.Services.AddHealthChecks();

var app = builder.Build();

await app.Services.InitializeBankDatabaseAsync(app.Configuration.GetValue("Bank:SeedSampleData", false));

// Malformed JSON raises BadHttpRequestException, which carries its own 400.
// Without the selector the exception handler would turn it into a 500 in Development.
app.UseExceptionHandler(new ExceptionHandlerOptions
{
    StatusCodeSelector = exception => exception is BadHttpRequestException badRequest
        ? badRequest.StatusCode
        : StatusCodes.Status500InternalServerError,
});
app.UseStatusCodePages();

app.MapOpenApi();
app.MapScalarApiReference(); // interactive API reference at /scalar
app.MapGet("/", () => Results.Redirect("/scalar")).ExcludeFromDescription();
app.MapHealthChecks("/health");
app.MapAccountEndpoints();

await app.RunAsync();

/// <summary>Entry point, exposed for integration tests (WebApplicationFactory).</summary>
public partial class Program;
