namespace Tutorial.Presentation.Modules.Main;

/// <summary>A scope shown in the dialog slot: it must know how to close.</summary>
public interface IDialogScope
{
    Func<Task> OnClose { get; }
}
