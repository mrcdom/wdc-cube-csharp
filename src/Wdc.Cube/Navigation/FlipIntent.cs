using System.Globalization;
using Wdc.Cube.Internal;

namespace Wdc.Cube;

/// <summary>
/// The request to bring the application to a face of the cube: a <see cref="Place"/>, the <b>parameters</b> that
/// identify the state (they go to the address) and the <b>attributes</b> that carry the context of the assembly
/// (slots, references, data already in hand; they never go to the address).
/// </summary>
/// <remarks>
/// The same intent travels through every <see cref="ICubePresenter.ApplyParameters"/> on the path, from the root to
/// the leaf. That is why parameter and attribute names must be unique across the application, each with one owner.
/// </remarks>
public sealed class FlipIntent
{
    private readonly OrderedDictionary<string, string[]> _parameters = new(StringComparer.Ordinal);

    /// <summary>Creates an intent for <paramref name="place"/> with no parameters or attributes.</summary>
    public FlipIntent(Place place)
    {
        ArgumentNullException.ThrowIfNull(place);
        Place = place;
    }

    /// <summary>The face this intent asks for.</summary>
    public Place Place { get; }

    /// <summary>Context of the assembly: never published to the address.</summary>
    public IDictionary<string, object?> Attributes { get; } = new Dictionary<string, object?>(StringComparer.Ordinal);

    /// <summary>The names of the parameters, in insertion order.</summary>
    public IEnumerable<string> ParameterNames => _parameters.Keys;

    /// <summary>Whether there are no parameters.</summary>
    public bool HasNoParameters => _parameters.Count == 0;

    // :: Parsing and formatting

    /// <summary>
    /// Reads <c>path?query</c>. The path is resolved by <paramref name="placeResolver"/>; with none, it becomes a
    /// detached place carrying that name.
    /// </summary>
    public static FlipIntent Parse(string? text, Func<string, Place>? placeResolver = null)
    {
        if (string.IsNullOrEmpty(text))
        {
            return new FlipIntent(Place.Unknown);
        }

        var question = text.IndexOf('?', StringComparison.Ordinal);
        var path = question < 0 ? text : text[..question];
        var place = placeResolver is null ? DetachedPlace(path) : placeResolver(path);
        var intent = new FlipIntent(place);
        if (question >= 0)
        {
            QueryString.Parse(text[(question + 1)..], intent.AddParameterValue);
        }

        return intent;
    }

    /// <summary>The parameters as a query string (without <c>?</c>); empty when there are none.</summary>
    public string GetQueryString() => _parameters.Count == 0 ? string.Empty : QueryString.Build(_parameters);

    /// <summary><c>path?query</c>, the form an address carries.</summary>
    public override string ToString()
    {
        var query = GetQueryString();
        return query.Length == 0 ? Place.PathName : Place.PathName + "?" + query;
    }

    // :: Derivation

    /// <summary>A new intent for <paramref name="place"/> carrying a copy of these parameters and attributes.</summary>
    public FlipIntent Redirect(Place place)
    {
        var other = new FlipIntent(place);
        other.CopyParametersFrom(this);
        other.CopyAttributesFrom(this);
        return other;
    }

    /// <summary>Copies every parameter of <paramref name="source"/>, replacing those with the same name.</summary>
    public void CopyParametersFrom(FlipIntent source)
    {
        ArgumentNullException.ThrowIfNull(source);
        foreach (var (name, values) in source._parameters)
        {
            _parameters[name] = values;
        }
    }

    /// <summary>Copies every attribute of <paramref name="source"/>, replacing those with the same name.</summary>
    public void CopyAttributesFrom(FlipIntent source)
    {
        ArgumentNullException.ThrowIfNull(source);
        CopyAttributesFrom(source.Attributes);
    }

    /// <summary>Copies every attribute of <paramref name="source"/>, replacing those with the same name.</summary>
    public void CopyAttributesFrom(IEnumerable<KeyValuePair<string, object?>>? source)
    {
        if (source is null)
        {
            return;
        }

        foreach (var (name, value) in source)
        {
            Attributes[name] = value;
        }
    }

    // :: Parameters — writing. A null value removes the parameter: the default state stays out of the address.

    /// <summary>Sets or removes (<see langword="null"/>) a text parameter.</summary>
    public void SetParameter(string name, string? value) => Store(name, value is null ? null : [value]);

    /// <summary>Sets or removes (<see langword="null"/>) an integer parameter.</summary>
    public void SetParameter(string name, long? value) =>
        Store(name, value is null ? null : [value.Value.ToString(CultureInfo.InvariantCulture)]);

    /// <summary>Sets or removes (<see langword="null"/>) a numeric parameter.</summary>
    public void SetParameter(string name, double? value) =>
        Store(name, value is null ? null : [FormatDouble(value.Value)]);

    /// <summary>Sets or removes (<see langword="null"/>) a boolean parameter.</summary>
    public void SetParameter(string name, bool? value) =>
        Store(name, value is null ? null : [value.Value ? "true" : "false"]);

    /// <summary>Sets or removes (<see langword="null"/> or empty) a multivalued text parameter.</summary>
    public void SetParameter(string name, IEnumerable<string>? values) => Store(name, values?.ToArray());

    /// <summary>Sets or removes (<see langword="null"/> or empty) a multivalued integer parameter.</summary>
    public void SetParameter(string name, IEnumerable<long>? values) =>
        Store(name, values?.Select(static v => v.ToString(CultureInfo.InvariantCulture)).ToArray());

    /// <summary>Sets or removes (<see langword="null"/> or empty) a multivalued numeric parameter.</summary>
    public void SetParameter(string name, IEnumerable<double>? values) =>
        Store(name, values?.Select(FormatDouble).ToArray());

    /// <summary>Sets or removes (<see langword="null"/> or empty) a multivalued boolean parameter.</summary>
    public void SetParameter(string name, IEnumerable<bool>? values) =>
        Store(name, values?.Select(static v => v ? "true" : "false").ToArray());

    /// <summary>Removes a parameter.</summary>
    public bool RemoveParameter(string name) => _parameters.Remove(name);

    // :: Parameters — reading. Absent or unreadable values come back as null (or the default given).

    /// <summary>Whether the parameter is present.</summary>
    public bool HasParameter(string name) => _parameters.ContainsKey(name);

    /// <summary>Every value of the parameter; empty when absent.</summary>
    public IReadOnlyList<string> GetValues(string name) => _parameters.TryGetValue(name, out var v) ? v : [];

    /// <summary>The first value of the parameter, or <see langword="null"/>.</summary>
    public string? GetString(string name) => _parameters.TryGetValue(name, out var v) && v.Length > 0 ? v[0] : null;

    /// <summary>The first value of the parameter, or <paramref name="defaultValue"/>.</summary>
    public string GetString(string name, string defaultValue) => GetString(name) ?? defaultValue;

    /// <summary>The first value as an integer, or <see langword="null"/> when absent or not an integer.</summary>
    public long? GetInt64(string name) =>
        long.TryParse(GetString(name), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : null;

    /// <summary>The first value as an integer, or <paramref name="defaultValue"/>.</summary>
    public long GetInt64(string name, long defaultValue) => GetInt64(name) ?? defaultValue;

    /// <summary>The first value as a 32-bit integer, or <see langword="null"/> when absent or out of range.</summary>
    public int? GetInt32(string name) =>
        int.TryParse(GetString(name), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : null;

    /// <summary>The first value as a 32-bit integer, or <paramref name="defaultValue"/>.</summary>
    public int GetInt32(string name, int defaultValue) => GetInt32(name) ?? defaultValue;

    /// <summary>The first value as a number, or <see langword="null"/> when absent or not a finite number.</summary>
    public double? GetDouble(string name) =>
        double.TryParse(GetString(name), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) && double.IsFinite(v)
            ? v
            : null;

    /// <summary>The first value as a number, or <paramref name="defaultValue"/>.</summary>
    public double GetDouble(string name, double defaultValue) => GetDouble(name) ?? defaultValue;

    /// <summary>The first value as a boolean (<c>true</c>/<c>false</c>), or <see langword="null"/>.</summary>
    public bool? GetBoolean(string name) => bool.TryParse(GetString(name), out var v) ? v : null;

    /// <summary>The first value as a boolean, or <paramref name="defaultValue"/>.</summary>
    public bool GetBoolean(string name, bool defaultValue) => GetBoolean(name) ?? defaultValue;

    // :: Attributes

    /// <summary>The attribute as <typeparamref name="T"/>, or <see langword="default"/> when absent or of another type.</summary>
    public T? GetAttribute<T>(string name) => Attributes.TryGetValue(name, out var v) && v is T typed ? typed : default;

    /// <summary>Sets or removes (<see langword="null"/>) an attribute.</summary>
    public void SetAttribute(string name, object? value)
    {
        if (value is null)
        {
            Attributes.Remove(name);
        }
        else
        {
            Attributes[name] = value;
        }
    }

    /// <summary>Offers a slot to the level below. <see langword="null"/> removes it.</summary>
    public void SetScopeSlot(string slotId, ScopeSlot? slot) => SetAttribute(slotId, slot);

    /// <summary>The slot offered by the level above, or a no-op slot when there is none.</summary>
    public ScopeSlot GetScopeSlot(string slotId) => GetAttribute<ScopeSlot>(slotId) ?? ScopeSlots.Noop;

    // :: Internals

    private void Store(string name, string[]? values)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        if (values is null || values.Length == 0)
        {
            _parameters.Remove(name);
        }
        else
        {
            _parameters[name] = values;
        }
    }

    private void AddParameterValue(string name, string value) =>
        _parameters[name] = _parameters.TryGetValue(name, out var current) ? [.. current, value] : [value];

    private static string FormatDouble(double value)
    {
        if (!double.IsFinite(value))
        {
            throw new ArgumentOutOfRangeException(nameof(value), value, "Only finite numbers can be parameters.");
        }

        return value.ToString("R", CultureInfo.InvariantCulture);
    }

    // Not cached: the text comes from outside (the address), and a cache keyed by it would grow without bound.
    private static Place DetachedPlace(string path) => path.Length == 0 ? Place.Unknown : Place.CreateDetached(path);
}
