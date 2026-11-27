using Wdc.Cube;

namespace Shopping.Presentation.Modules.Login;

public sealed class LoginScope : Scope
{
    public string? UserName { get; set => Set(ref field, value); }

    public Message? ErrorMessage { get; set => Set(ref field, value); }

    /// <summary>While checking the credentials: the view shows a spinner and disables the form.</summary>
    public bool Loading { get; set => Set(ref field, value); }

    /// <summary>(userName, password). The password never goes to the scope.</summary>
    public Func<string, string, Task> OnEnter { get; set; } = ScopeActions.Unbound<string, string>();
}
