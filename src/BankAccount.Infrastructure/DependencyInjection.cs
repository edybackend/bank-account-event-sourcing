using BankAccount.Application.Abstractions;
using BankAccount.Application.Queries;
using BankAccount.Infrastructure.EventStore;
using BankAccount.Infrastructure.ReadModel;
using Dapper;
using Microsoft.Extensions.DependencyInjection;

namespace BankAccount.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddBankInfrastructure(this IServiceCollection services, string connectionString)
    {
        services.AddSingleton(new SqliteDatabase(connectionString));
        services.AddSingleton<IEventStore, SqliteEventStore>();
        services.AddSingleton<IAccountReadModel, SqliteAccountReadModel>();

        return services;
    }

    /// <summary>Creates the schema and, optionally, loads sample accounts into an empty event store.</summary>
    public static async Task InitializeBankDatabaseAsync(this IServiceProvider services, bool seedSampleData, CancellationToken cancellationToken = default)
    {
        var database = services.GetRequiredService<SqliteDatabase>();
        await database.CreateSchemaAsync(cancellationToken);

        if (!seedSampleData)
        {
            return;
        }

        await using (var connection = await database.OpenAsync(cancellationToken))
        {
            var hasEvents = await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
                "SELECT EXISTS (SELECT 1 FROM events)", cancellationToken: cancellationToken));
            if (hasEvents)
            {
                return;
            }
        }

        await SampleData.SeedAsync(
            services.GetRequiredService<IEventStore>(),
            services.GetRequiredService<TimeProvider>().GetUtcNow(),
            cancellationToken);
    }
}
