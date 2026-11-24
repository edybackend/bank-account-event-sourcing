using BankAccount.Application.Commands;
using BankAccount.Application.Queries;
using Microsoft.Extensions.DependencyInjection;

namespace BankAccount.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddBankApplication(this IServiceCollection services)
    {
        services.AddSingleton(TimeProvider.System);
        services.AddScoped<AccountCommandHandler>();
        services.AddScoped<AccountHistory>();
        return services;
    }
}
