using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace BankAccount.IntegrationTests;

/// <summary>Runs the real API, event store and projections against a throw-away SQLite file.</summary>
public class BankApiFactory : WebApplicationFactory<Program>
{
    private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"bank-tests-{Guid.NewGuid():N}.db");

    protected virtual bool SeedSampleData => false;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:Bank", $"Data Source={_databasePath};Pooling=False");
        builder.UseSetting("Bank:SeedSampleData", SeedSampleData.ToString());
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        foreach (var file in new[] { _databasePath, _databasePath + "-wal", _databasePath + "-shm" })
        {
            File.Delete(file);
        }
    }
}

public sealed class SeededBankApiFactory : BankApiFactory
{
    protected override bool SeedSampleData => true;
}
