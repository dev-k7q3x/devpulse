namespace DevPulse.Domain.ValueObjects;

/// <summary>
/// Nazwa repozytorium <c>owner/name</c>. Może się zmienić (rename, transfer) – repo identyfikujemy po id GitHuba,
/// a nazwa służy tylko do wyświetlania i linków.
/// </summary>
public sealed record RepoFullName
{
    public const int OwnerMaxLength = 39;
    public const int NameMaxLength = 100;

    public RepoFullName(string owner, string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(owner);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        if (owner.Length > OwnerMaxLength || owner.Contains('/'))
        {
            throw new ArgumentException($"Nieprawidłowy właściciel repozytorium: '{owner}'.", nameof(owner));
        }

        if (name.Length > NameMaxLength || name.Contains('/'))
        {
            throw new ArgumentException($"Nieprawidłowa nazwa repozytorium: '{name}'.", nameof(name));
        }

        Owner = owner;
        Name = name;
    }

    public string Owner { get; }

    public string Name { get; }

    public string Value => $"{Owner}/{Name}";

    public static RepoFullName Parse(string fullName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fullName);

        var separator = fullName.IndexOf('/');
        if (separator <= 0 || separator != fullName.LastIndexOf('/'))
        {
            throw new FormatException($"Oczekiwano formatu 'owner/name', otrzymano '{fullName}'.");
        }

        return new RepoFullName(fullName[..separator], fullName[(separator + 1)..]);
    }

    public override string ToString() => Value;
}
