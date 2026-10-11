namespace BrokerLedger.Domain;

/// <summary>Klient domu maklerskiego. Haslo tylko jako hash — domena nie wie, jakim algorytmem.</summary>
public sealed record ClientAccount(ClientId Id, EmailAddress Email, string PasswordHash)
{
    public ClientId Id { get; } = Id ?? throw new ArgumentNullException(nameof(Id));
    public EmailAddress Email { get; } = Email ?? throw new ArgumentNullException(nameof(Email));
    public string PasswordHash { get; } = string.IsNullOrWhiteSpace(PasswordHash)
        ? throw new ArgumentException("password hash must not be empty", nameof(PasswordHash))
        : PasswordHash;
}

public interface IClientRepository
{
    /// <summary>Zapisuje nowego klienta; <c>false</c>, gdy ten e-mail jest juz zajety.</summary>
    Task<bool> TryAddAsync(ClientAccount client, CancellationToken cancellationToken = default);

    Task<ClientAccount?> FindByEmailAsync(EmailAddress email, CancellationToken cancellationToken = default);

    /// <summary>Podmienia hash hasla — np. po podniesieniu liczby iteracji, przy najblizszym logowaniu.</summary>
    Task UpdatePasswordHashAsync(ClientId client, string passwordHash, CancellationToken cancellationToken = default);
}
