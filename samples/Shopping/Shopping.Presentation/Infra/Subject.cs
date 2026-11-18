namespace Shopping.Presentation.Infra;

/// <summary>Who is logged in: what the screens need, not the user record.</summary>
/// <param name="Id">The user.</param>
/// <param name="NickName">How the screens greet them.</param>
/// <param name="Credential">What takes the session back after a reload, when the authentication needs one (a refresh
/// token through HTTP); opaque to the presentation.</param>
/// <param name="HistoryKey">The user's address key (Base64url, decision D-22): with it the addresses are sealed.</param>
public sealed record Subject(long Id, string NickName, string? Credential = null, string? HistoryKey = null);
