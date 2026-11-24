namespace BankAccount.Domain;

/// <summary>Thrown when a command would break a business rule of the account.</summary>
public sealed class DomainException(string message) : Exception(message);
