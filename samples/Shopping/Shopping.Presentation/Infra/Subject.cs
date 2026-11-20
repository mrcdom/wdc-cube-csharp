using Shopping.Domain.Security;

namespace Shopping.Presentation.Infra;

/// <summary>Who is logged in: what the screens need, not the user record.</summary>
/// <param name="Id">The user.</param>
/// <param name="NickName">How the screens greet them.</param>
/// <param name="Credential">What takes the session back after a reload, when the authentication needs one (a refresh
/// token through HTTP); opaque to the presentation.</param>
/// <param name="HistoryKey">The user's address key (Base64url): with it the addresses are sealed.</param>
/// <param name="Roles">The user's roles, comma-separated as the user record keeps them: what the screens offer (the
/// repositories enforce the rules whatever the screens offer).</param>
public sealed record Subject(long Id, string NickName, string? Credential = null, string? HistoryKey = null, string? Roles = null)
{
    /// <summary>Whether the roles grant <paramref name="permission"/>: what to offer (the repositories decide).</summary>
    public bool Can(string permission) => Permissions.Has(Permissions.ActorOf(Id, Roles), permission);
}
