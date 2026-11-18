namespace Wdc.Cube;

/// <summary>
/// Creates the presenter that owns a <see cref="Place"/>, for a given application.
/// </summary>
public delegate ICubePresenter PresenterFactory(CubeApplication app);

/// <summary>
/// A node of the cube: one of the positions a presenter can occupy. Places form a tree, and the path from the
/// root to a place names a face of the cube (a composition of presenters).
/// </summary>
/// <remarks>
/// <para>
/// A place is an immutable definition, so it can live in a static field and be shared by every application in the
/// process. What changes at run time (which presenters are alive, where the application is) belongs to each
/// <see cref="CubeApplication"/>.
/// </para>
/// <para>
/// Ids grow in creation order, and a place can only be created after its parent, so a child always has a larger id
/// than its ancestors. Releasing presenters in descending id order therefore releases leaves first.
/// </para>
/// </remarks>
public sealed class Place
{
    private static int s_lastId = -1;

    /// <summary>
    /// The detached root every top-level place hangs from. It is not part of any path, and its address is the empty
    /// string.
    /// </summary>
    public static Place Root { get; } = new(string.Empty, parent: null, factory: null, id: -1);

    /// <summary>A detached place for intents whose place could not be resolved.</summary>
    public static Place Unknown { get; } = new("unknown", parent: null, factory: null, id: -1);

    private Place(string name, Place? parent, PresenterFactory? factory, int id)
    {
        Name = name;
        Parent = parent;
        Factory = factory;
        Id = id;

        var path = new List<Place>();
        CollectPath(path, this);
        Path = path;
        PathName = path.Count == 0 ? name : string.Join('/', path.Select(static p => p.Name));
    }

    /// <summary>Unique id. Negative for detached places.</summary>
    public int Id { get; }

    /// <summary>The name of this step of the path.</summary>
    public string Name { get; }

    /// <summary>The place this one is nested in.</summary>
    public Place? Parent { get; }

    /// <summary>Creates the owning presenter; <see langword="null"/> for a purely structural place.</summary>
    public PresenterFactory? Factory { get; }

    /// <summary>The places from the root down to this one, detached places excluded.</summary>
    public IReadOnlyList<Place> Path { get; }

    /// <summary>The path names joined by <c>/</c>. This is what an address carries.</summary>
    public string PathName { get; }

    /// <summary>Whether this place is outside the tree (<see cref="Root"/>, <see cref="Unknown"/>).</summary>
    public bool IsDetached => Id < 0;

    /// <summary>Creates a top-level place (a child of <see cref="Root"/>).</summary>
    public static Place Create(string name, PresenterFactory? factory = null) => Root.Child(name, factory);

    /// <summary>Creates a top-level place whose presenter needs a specific application type.</summary>
    public static Place Create<TApp>(string name, Func<TApp, ICubePresenter> factory)
        where TApp : CubeApplication => Root.Child(name, factory);

    /// <summary>Creates a place nested in this one.</summary>
    public Place Child(string name, PresenterFactory? factory = null)
    {
        ValidateName(name);
        return new Place(name, this, factory, Interlocked.Increment(ref s_lastId));
    }

    /// <summary>Creates a place nested in this one whose presenter needs a specific application type.</summary>
    public Place Child<TApp>(string name, Func<TApp, ICubePresenter> factory)
        where TApp : CubeApplication
    {
        ArgumentNullException.ThrowIfNull(factory);
        return Child(name, app => app is TApp typed
            ? factory(typed)
            : throw new InvalidOperationException(
                $"Place '{name}' needs an application of type {typeof(TApp).Name}, but got {app.GetType().Name}."));
    }

    /// <summary>
    /// A place outside the tree that only carries a name: what an address resolves to when nothing knows the path.
    /// It has no presenter and no path.
    /// </summary>
    public static Place CreateDetached(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        return new Place(name, parent: null, factory: null, id: -1);
    }

    /// <summary>Whether <paramref name="other"/> is this place or nested somewhere below it.</summary>
    public bool Contains(Place other)
    {
        ArgumentNullException.ThrowIfNull(other);
        if (Path.Count > other.Path.Count)
        {
            return false;
        }

        for (var i = 0; i < Path.Count; i++)
        {
            if (!ReferenceEquals(Path[i], other.Path[i]))
            {
                return false;
            }
        }

        return true;
    }

    /// <inheritdoc/>
    public override string ToString() => PathName;

    private static void CollectPath(List<Place> path, Place step)
    {
        if (step.Parent is not null)
        {
            CollectPath(path, step.Parent);
        }

        if (!step.IsDetached)
        {
            path.Add(step);
        }
    }

    private static void ValidateName(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (name.AsSpan().IndexOfAny("/?&#=") >= 0)
        {
            throw new ArgumentException($"A place name cannot contain '/', '?', '&', '#' or '=': '{name}'.", nameof(name));
        }
    }
}
