using Tutorial.Presentation.Modules.Main;
using Wdc.Cube;

namespace Tutorial.Presentation.Modules.TodoMvc;

public sealed class TodoMvcKeys : MainKeys
{
    public new const string Prefix = "tm";

    public static new Place Place { get; } = MainKeys.Place.Child<MainPresenter>("todos", app => new TodoMvcPresenter(app));

    private static class P
    {
        /// <summary>Which list: 0 the sample, a negative id the 1000-item generator, a positive id a user's.</summary>
        public const string UserId = $"{Prefix}-uid";

        /// <summary>The filter.</summary>
        public const string Showing = $"{Prefix}-showing";
    }

    public TodoMvcKeys(CubeApplication app, FlipIntent? intent = null)
        : base(app, intent, Place)
    {
    }

    /// <summary>The list id; 0 (the default, kept out of the address) when absent or unreadable.</summary>
    public long UserId
    {
        get => Intent.GetInt64(P.UserId) ?? 0;
        set => Intent.SetParameter(P.UserId, value == 0 ? null : value);
    }

    /// <summary>A closed choice: anything outside the allowlist reads as <see cref="ShowingOptions.All"/>.</summary>
    public ShowingOptions Showing
    {
        get => Intent.GetInt32(P.Showing) is { } value && Enum.IsDefined((ShowingOptions)value)
            ? (ShowingOptions)value
            : ShowingOptions.All;
        set => Intent.SetParameter(P.Showing, value == ShowingOptions.All ? null : (long)value);
    }
}
