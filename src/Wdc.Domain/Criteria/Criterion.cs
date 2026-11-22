using System.Text.Json;

namespace Wdc.Domain;

/// <summary>A filter on one field: the requests made on it, combined by AND, or by OR after <c>Or()</c>.</summary>
public abstract class Criterion
{
    protected Criterion(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name;
    }

    /// <summary>The field's name (in JSON and for the repository's translation).</summary>
    public string Name { get; }

    /// <summary>Whether the requests on this field combine with OR.</summary>
    public bool Disjunctive { get; protected set; }

    /// <summary>Whether anything was asked; an empty criterion does not filter.</summary>
    public abstract bool IsSet { get; }

    public abstract IReadOnlyList<IPredicate> Predicates { get; }

    /// <summary>
    /// Requests set by the server (the reach of the caller: "only the purchases of user 7"). They never travel on the
    /// wire and always combine with AND, outside the requests of <see cref="Predicates"/>: a client that marks the
    /// field disjunctive (<c>"or": true</c>) widens only its own requests, never the restriction.
    /// </summary>
    public abstract IReadOnlyList<IPredicate> Restrictions { get; }

    /// <summary>Whether the server restricted this field.</summary>
    public bool IsRestricted => Restrictions.Count > 0;

    public abstract IValueKind ValueKind { get; }

    internal abstract void WriteJson(Utf8JsonWriter writer);

    internal abstract void ReadJson(ref Utf8JsonReader reader);

    public override string ToString()
    {
        var text = !IsSet ? $"{Name} (not set)" : $"{Name} {(Disjunctive ? "any of" : "all of")} [{Describe(Predicates)}]";
        return IsRestricted ? $"{text} restricted to [{Describe(Restrictions)}]" : text;
    }

    private static string Describe(IEnumerable<IPredicate> predicates) =>
        string.Join(", ", predicates.Select(p =>
            p.Values.Count == 0 ? p.Operator.WireName() : $"{p.Operator.WireName()} [{string.Join(", ", p.Values)}]"));
}

/// <summary>
/// A filter on a field compared only for identity (an enum, a flag, a foreign key): equal, not equal, in, null.
/// </summary>
/// <typeparam name="TOwner">The criteria object it belongs to; fluent calls return it.</typeparam>
/// <typeparam name="T">The nullable value type (<c>long?</c>, <c>string?</c>...).</typeparam>
public class Criterion<TOwner, T> : Criterion
    where TOwner : class
{
    private readonly TOwner _owner;
    private readonly ValueKind<T> _kind;
    private readonly List<Predicate> _requests = [];
    private readonly List<Predicate> _restrictions = [];

    public Criterion(TOwner owner, string name, ValueKind<T> kind)
        : base(name)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(kind);
        _owner = owner;
        _kind = kind;
    }

    public override bool IsSet => _requests.Count > 0;

    public override IReadOnlyList<IPredicate> Predicates => _requests;

    public override IReadOnlyList<IPredicate> Restrictions => _restrictions;

    public override IValueKind ValueKind => _kind;

    /// <summary>The kind of the values.</summary>
    public ValueKind<T> Kind => _kind;

    /// <summary>The criteria object this criterion belongs to.</summary>
    protected TOwner Owner => _owner;

    /// <summary>The following requests on this field combine with OR.</summary>
    public virtual Criterion<TOwner, T> Or()
    {
        Disjunctive = true;
        return this;
    }

    /// <summary>The requests on this field combine with AND (the default).</summary>
    public virtual Criterion<TOwner, T> And()
    {
        Disjunctive = false;
        return this;
    }

    /// <summary>
    /// Restricts the field to <paramref name="value"/> whatever else was asked (see <see cref="Criterion.Restrictions"/>).
    /// <see cref="Clear"/> does not remove it.
    /// </summary>
    public TOwner Restrict(T value)
    {
        if (ValueKind<T>.IsNull(value))
        {
            throw new ArgumentNullException(nameof(value), "A restriction needs a value: restricting to nothing is no restriction.");
        }

        _restrictions.Add(new Predicate(Operator.Eq, [value]));
        return _owner;
    }

    /// <summary>Restricts the field to one of <paramref name="values"/>; an empty list matches nothing.</summary>
    public TOwner RestrictIn(IEnumerable<T> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        _restrictions.Add(new Predicate(Operator.In, [.. values]));
        return _owner;
    }

    /// <summary>Removes every request (not the restrictions).</summary>
    public TOwner Clear()
    {
        _requests.Clear();
        return _owner;
    }

    /// <summary>Equal to <paramref name="value"/>; a null value asks nothing (an empty screen field does not filter).</summary>
    public TOwner Eq(T value) => ValueKind<T>.IsNull(value) ? _owner : Add(Operator.Eq, value);

    /// <summary>Different from <paramref name="value"/>; a null value asks nothing.</summary>
    public TOwner Ne(T value) => ValueKind<T>.IsNull(value) ? _owner : Add(Operator.Ne, value);

    /// <summary>
    /// One of <paramref name="values"/>. <see langword="null"/> asks nothing ("not informed"); an empty list matches
    /// nothing (a search finds no row and a delete removes nothing).
    /// </summary>
    public TOwner In(IEnumerable<T>? values)
    {
        if (values is null)
        {
            return _owner;
        }

        _requests.Add(new Predicate(Operator.In, [.. values]));
        return _owner;
    }

    /// <summary>One of <paramref name="values"/>.</summary>
    public TOwner In(params T[] values) => In((IEnumerable<T>)values);

    public TOwner IsNull() => Add(Operator.IsNull);

    public TOwner IsNotNull() => Add(Operator.IsNotNull);

    /// <summary>
    /// Puts back a request read from the wire, as it was: unlike the fluent methods, it keeps an explicit null.
    /// </summary>
    /// <exception cref="InvalidRequestException">
    /// The request cannot be a filter: the wrong number of values for the operator, a null where only a value
    /// compares (<c>GT</c>, <c>BETWEEN</c>, <c>LIKE</c>...), or a pattern on a field that is not text. Kept, it would
    /// count as a filter and translate to none, and a delete would reach every row.
    /// </exception>
    public void Restore(Operator op, IReadOnlyList<T>? values)
    {
        T[] list = [.. values ?? []];
        if (!op.Accepts(list.Length) || (op.ComparesValues() && list.Any(v => ValueKind<T>.IsNull(v)))
            || (op is Operator.Like or Operator.ILike && typeof(T) != typeof(string)))
        {
            throw new InvalidRequestException(
                $"'{Name}': {op.WireName()} with [{string.Join(", ", list.Select(v => v?.ToString() ?? "null"))}] is not a filter.");
        }

        _requests.Add(new Predicate(op, list));
    }

    /// <summary>Puts back the combination read from the wire.</summary>
    public void RestoreDisjunctive(bool value) => Disjunctive = value;

    /// <summary>The typed requests.</summary>
    public IEnumerable<(Operator Operator, IReadOnlyList<T> Values)> TypedPredicates =>
        _requests.Select(p => (p.Operator, (IReadOnlyList<T>)p.TypedValues));

    protected TOwner Add(Operator op, params T[] values)
    {
        _requests.Add(new Predicate(op, values));
        return _owner;
    }

    internal override void WriteJson(Utf8JsonWriter writer)
    {
        writer.WriteStartObject(Name);
        if (Disjunctive)
        {
            writer.WriteBoolean("or", true);
        }

        writer.WriteStartArray("p");
        foreach (var predicate in _requests)
        {
            writer.WriteStartObject();
            writer.WriteString("o", predicate.Operator.WireName());
            if (predicate.TypedValues.Length > 0)
            {
                writer.WriteStartArray("v");
                foreach (var value in predicate.TypedValues)
                {
                    if (ValueKind<T>.IsNull(value))
                    {
                        writer.WriteNullValue();
                    }
                    else
                    {
                        _kind.Write(writer, value);
                    }
                }

                writer.WriteEndArray();
            }

            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    // At the value token. A loose value is equality; a loose null asks nothing.
    internal override void ReadJson(ref Utf8JsonReader reader)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
        {
            var loose = ReadValue(ref reader);
            if (!ValueKind<T>.IsNull(loose))
            {
                Restore(Operator.Eq, [loose]);
            }

            SkipIfContainer(ref reader);
            return;
        }

        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            var property = reader.GetString();
            reader.Read();
            switch (property)
            {
                case "or":
                    RestoreDisjunctive(reader.TokenType == JsonTokenType.True);
                    break;
                case "p" when reader.TokenType == JsonTokenType.StartArray:
                    ReadPredicates(ref reader);
                    break;
                default:
                    reader.Skip();
                    break;
            }
        }
    }

    private void ReadPredicates(ref Utf8JsonReader reader)
    {
        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            if (reader.TokenType != JsonTokenType.StartObject)
            {
                reader.Skip();
                continue;
            }

            Operator? op = null;
            var values = new List<T>(2);
            while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
            {
                var property = reader.GetString();
                reader.Read();
                switch (property)
                {
                    case "o":
                        op = Operators.TryParseWireName(reader.TokenType == JsonTokenType.String ? reader.GetString() : null,
                            out var parsed)
                            ? parsed
                            : null;
                        break;
                    case "v" when reader.TokenType == JsonTokenType.StartArray:
                        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
                        {
                            values.Add(ReadValue(ref reader));
                            SkipIfContainer(ref reader);
                        }

                        break;
                    default:
                        reader.Skip();
                        break;
                }
            }

            if (op is { } known)
            {
                Restore(known, values); // unknown operator: discarded (a looser filter, never a different one)
            }
        }
    }

    // A null token is null; any other token that does not convert is refused, never read as null (EQ would become
    // IS NULL).
    private T ReadValue(ref Utf8JsonReader reader)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            return default!;
        }

        var value = _kind.Read(ref reader);
        if (ValueKind<T>.IsNull(value))
        {
            throw new InvalidRequestException($"'{Name}': {reader.TokenType} is not a value of this field.");
        }

        return value;
    }

    private static void SkipIfContainer(ref Utf8JsonReader reader)
    {
        if (reader.TokenType is JsonTokenType.StartObject or JsonTokenType.StartArray)
        {
            reader.Skip();
        }
    }

    private sealed class Predicate(Operator op, T[] values) : IPredicate
    {
        public Operator Operator { get; } = op;

        public T[] TypedValues { get; } = values;

        public IReadOnlyList<object?> Values { get; } = [.. values.Select(v => (object?)v)];
    }
}
