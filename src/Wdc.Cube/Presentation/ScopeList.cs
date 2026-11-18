using System.Collections;

namespace Wdc.Cube;

/// <summary>
/// A list held by a scope whose structural changes mark that scope dirty (the equivalent of <c>ObservableArray</c>).
/// Declare it as a read-only property: <c>public ScopeList&lt;RowScope&gt; Rows { get; } = new(this)</c> is not valid
/// C#, so create it in the constructor: <c>Rows = new(this)</c>.
/// </summary>
/// <remarks>
/// Items that are scopes are adopted by the owner's manager. Replacing an item with the same instance, or assigning
/// the same sequence of instances, changes nothing: reuse row scopes across loads so the view keeps what is on
/// screen (reconcile by <see cref="Scope.Identity"/>).
/// </remarks>
public sealed class ScopeList<T> : IReadOnlyList<T>
{
    private readonly Scope _owner;
    private readonly string? _propertyName;
    private readonly List<T> _items = [];

    /// <summary>Creates an empty list owned by <paramref name="owner"/>.</summary>
    /// <param name="owner">The scope marked dirty by structural changes.</param>
    /// <param name="propertyName">The owner's property, reported to property-change listeners.</param>
    public ScopeList(Scope owner, string? propertyName = null)
    {
        ArgumentNullException.ThrowIfNull(owner);
        _owner = owner;
        _propertyName = propertyName;
    }

    /// <inheritdoc/>
    public int Count => _items.Count;

    /// <inheritdoc/>
    public T this[int index] => _items[index];

    /// <summary>Appends an item.</summary>
    public void Add(T item)
    {
        Adopt(item);
        _items.Add(item);
        Changed();
    }

    /// <summary>Inserts an item.</summary>
    public void Insert(int index, T item)
    {
        Adopt(item);
        _items.Insert(index, item);
        Changed();
    }

    /// <summary>Replaces the item at <paramref name="index"/>; nothing happens when it is the same instance.</summary>
    public void Set(int index, T item)
    {
        if (Same(_items[index], item))
        {
            return;
        }

        Adopt(item);
        _items[index] = item;
        Changed();
    }

    /// <summary>Removes the first occurrence of <paramref name="item"/>.</summary>
    public bool Remove(T item)
    {
        var index = _items.FindIndex(i => Same(i, item));
        if (index < 0)
        {
            return false;
        }

        RemoveAt(index);
        return true;
    }

    /// <summary>Removes the item at <paramref name="index"/>.</summary>
    public void RemoveAt(int index)
    {
        _items.RemoveAt(index);
        Changed();
    }

    /// <summary>Removes every item that matches; marks dirty only when something was removed.</summary>
    public int RemoveAll(Predicate<T> match)
    {
        var removed = _items.RemoveAll(match);
        if (removed > 0)
        {
            Changed();
        }

        return removed;
    }

    /// <summary>Removes every item.</summary>
    public void Clear()
    {
        if (_items.Count == 0)
        {
            return;
        }

        _items.Clear();
        Changed();
    }

    /// <summary>
    /// Makes the list hold exactly <paramref name="items"/>, in order. Marks dirty only when some position now holds
    /// another instance or the length changed.
    /// </summary>
    public void Assign(IEnumerable<T> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        var next = items as IReadOnlyList<T> ?? [.. items];

        var changed = next.Count != _items.Count;
        for (var i = 0; !changed && i < next.Count; i++)
        {
            changed = !Same(_items[i], next[i]);
        }

        if (!changed)
        {
            return;
        }

        foreach (var item in next)
        {
            Adopt(item);
        }

        _items.Clear();
        _items.AddRange(next);
        Changed();
    }

    /// <summary>Sorts in place and marks dirty.</summary>
    public void Sort(Comparison<T> comparison)
    {
        _items.Sort(comparison);
        Changed();
    }

    /// <summary>Index of <paramref name="item"/> (by reference for objects), or -1.</summary>
    public int IndexOf(T item) => _items.FindIndex(i => Same(i, item));

    /// <inheritdoc/>
    public IEnumerator<T> GetEnumerator() => _items.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    private void Adopt(T item)
    {
        if (item is Scope scope)
        {
            _owner.Adopt(scope);
        }
    }

    private void Changed() => _owner.MarkDirty(_propertyName);

    private static bool Same(T a, T b)
    {
        if (typeof(T).IsValueType)
        {
            return EqualityComparer<T>.Default.Equals(a, b);
        }

        return a is string text ? string.Equals(text, b as string, StringComparison.Ordinal) : ReferenceEquals(a, b);
    }
}
