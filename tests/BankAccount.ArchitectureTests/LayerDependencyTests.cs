using System.Reflection;
using BankAccount.Domain;
using NetArchTest.Rules;

namespace BankAccount.ArchitectureTests;

/// <summary>Clean Architecture dependency rule, enforced by the build.</summary>
public class LayerDependencyTests
{
    private const string Application = "BankAccount.Application";
    private const string Infrastructure = "BankAccount.Infrastructure";
    private const string Api = "BankAccount.Api";

    private static readonly Assembly DomainAssembly = typeof(Account).Assembly;
    private static readonly Assembly ApplicationAssembly = typeof(BankAccount.Application.Result).Assembly;
    private static readonly Assembly InfrastructureAssembly = typeof(BankAccount.Infrastructure.DependencyInjection).Assembly;
    private static readonly Assembly ApiAssembly = typeof(Program).Assembly;

    [Fact]
    public void Domain_depends_on_nothing_else() =>
        AssertNoDependency(DomainAssembly, Application, Infrastructure, Api, "Dapper", "Microsoft.Data.Sqlite", "Microsoft.Extensions", "System.Text.Json");

    [Fact]
    public void Application_does_not_depend_on_infrastructure_or_web_frameworks() =>
        AssertNoDependency(ApplicationAssembly, Infrastructure, Api, "Dapper", "Microsoft.Data.Sqlite", "Microsoft.AspNetCore");

    [Fact]
    public void Infrastructure_does_not_depend_on_the_api() =>
        AssertNoDependency(InfrastructureAssembly, Api, "Microsoft.AspNetCore");

    [Fact]
    public void Endpoints_never_touch_the_database()
    {
        var result = Types.InAssembly(ApiAssembly)
            .That().ResideInNamespace("BankAccount.Api.Endpoints")
            .ShouldNot().HaveDependencyOnAny(Infrastructure, "Dapper", "Microsoft.Data.Sqlite")
            .GetResult();

        Assert.True(result.IsSuccessful, Describe(result));
    }

    [Fact]
    public void Events_are_immutable_sealed_records() =>
        Assert.All(
            DomainAssembly.GetTypes().Where(t => t.IsSubclassOf(typeof(AccountEvent))),
            type =>
            {
                Assert.True(type.IsSealed, $"{type.Name} must be sealed");
                Assert.All(type.GetProperties(), p => Assert.True(p.SetMethod is null || p.SetMethod.ReturnParameter.GetRequiredCustomModifiers().Any(m => m.Name == "IsExternalInit"), $"{type.Name}.{p.Name} must be init-only"));
            });

    private static void AssertNoDependency(Assembly assembly, params string[] forbidden)
    {
        var result = Types.InAssembly(assembly).ShouldNot().HaveDependencyOnAny(forbidden).GetResult();

        Assert.True(result.IsSuccessful, Describe(result));
    }

    private static string Describe(NetArchTest.Rules.TestResult result) =>
        "Violations: " + string.Join(", ", result.FailingTypeNames ?? []);
}
