namespace Wdc.Domain;

/// <summary>
/// Who is acting: an identifier and the roles, as the application writes them. The framework only
/// compares them; what a role may do is the application's policies.
/// </summary>
public sealed class Actor
{
    public Actor(string id, IEnumerable<string> roles)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(roles);
        Id = id;
        Roles = new HashSet<string>(roles, StringComparer.Ordinal);
    }

    public string Id { get; }

    public IReadOnlySet<string> Roles { get; }

    public bool HasRole(string role) => Roles.Contains(role);

    public override string ToString() => $"{Id} [{string.Join(", ", Roles)}]";
}
