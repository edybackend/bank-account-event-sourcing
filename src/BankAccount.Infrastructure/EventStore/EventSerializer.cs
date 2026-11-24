using System.Text.Json;
using BankAccount.Domain;

namespace BankAccount.Infrastructure.EventStore;

/// <summary>
/// Maps stored type names to event classes explicitly. The name in the database is a contract:
/// renaming a C# class must not break old streams, and no type is ever loaded from a name found in the data.
/// </summary>
internal static class EventSerializer
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    private static readonly Dictionary<string, Type> TypesByName = new(StringComparer.Ordinal)
    {
        ["AccountOpened"] = typeof(AccountOpened),
        ["MoneyDeposited"] = typeof(MoneyDeposited),
        ["MoneyWithdrawn"] = typeof(MoneyWithdrawn),
        ["TransferSent"] = typeof(TransferSent),
        ["TransferReceived"] = typeof(TransferReceived),
        ["AccountClosed"] = typeof(AccountClosed),
    };

    private static readonly Dictionary<Type, string> NamesByType = TypesByName.ToDictionary(pair => pair.Value, pair => pair.Key);

    public static string TypeName(AccountEvent @event) =>
        NamesByType.TryGetValue(@event.GetType(), out var name)
            ? name
            : throw new InvalidOperationException($"Event {@event.GetType().Name} has no stored name.");

    public static string Serialize(AccountEvent @event) => JsonSerializer.Serialize(@event, @event.GetType(), Options);

    public static AccountEvent Deserialize(string typeName, string data) =>
        TypesByName.TryGetValue(typeName, out var type)
            ? (AccountEvent)(JsonSerializer.Deserialize(data, type, Options) ?? throw new InvalidOperationException($"Empty {typeName}."))
            : throw new InvalidOperationException($"Unknown stored event type '{typeName}'.");
}
