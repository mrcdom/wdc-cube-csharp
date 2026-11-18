namespace Shopping.Presentation.Infra;

/// <summary>
/// Opens and closes the session. Locally it checks the user table; through HTTP it answers the server's challenge and
/// keeps the tokens (decision D-19). The presenters do not know which.
/// </summary>
public interface IAuthentication
{
    /// <summary>The subject for these credentials, or <see langword="null"/> when they do not match.</summary>
    Task<Subject?> Login(string userName, string password, CancellationToken cancellationToken = default);

    /// <summary>
    /// Takes back a session read from the session store (a reload): the same subject, one with a renewed
    /// <see cref="Subject.Credential"/>, or <see langword="null"/> when it is no longer valid.
    /// </summary>
    Task<Subject?> Resume(Subject subject, CancellationToken cancellationToken = default);

    /// <summary>Ends the session (on the server too, when there is one).</summary>
    Task Logout(Subject subject, CancellationToken cancellationToken = default);
}
