using System.Text.Json;
using Wdc.Domain;

namespace Wdc.Rest;

/// <summary>
/// The bodies of the repository routes (the wire of the Kotlin version): written by the client, read by the server and
/// the other way round, from the same entity model.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>Queries: the criteria's fields, <c>orderBy</c>, <c>projection</c> and the slice in one object.</item>
/// <item><c>insert</c>: <c>{"entity":{…},"projection":{…}}</c>, or the entity alone →
/// <c>{"success":true,"id":7,"entity":{…}}</c>.</item>
/// <item><c>update</c>: the fields to write, explicit nulls included, plus the key → <c>{"success":true}</c>.</item>
/// <item><c>delete</c>/<c>count</c> → <c>{"count":N}</c>; <c>fetch</c> → <c>{"items":[…]}</c>;
/// <c>fetch-page</c> → <c>{"items":[…],"totalItems":N}</c>; <c>fetch-by-id</c> <c>{"id":7,"projection":{…}}</c> →
/// the entity, or 404.</item>
/// </list>
/// </remarks>
public sealed class RepositoryWire<TEntity, TCriteria>
    where TEntity : class
    where TCriteria : Criteria<TCriteria, TEntity>
{
    private const string ProjectionName = "projection";
    private const string EntityName = "entity";

    public RepositoryWire(EntityModel<TEntity> model, Func<TCriteria> newCriteria)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(newCriteria);
        if (model.Key is null)
        {
            throw new ArgumentException($"{model.Name} has no key: it cannot be served by the repository routes.", nameof(model));
        }

        Codec = new JsonEntityCodec<TEntity>(model);
        NewCriteria = newCriteria;
    }

    public JsonEntityCodec<TEntity> Codec { get; }

    public EntityModel<TEntity> Model => Codec.Model;

    public Func<TCriteria> NewCriteria { get; }

    // :: Queries

    public byte[] WriteQuery(TCriteria criteria, Slice slice = default)
    {
        ArgumentNullException.ThrowIfNull(criteria);
        return RestJson.Write(w =>
        {
            w.WriteStartObject();
            CriteriaCodec.WriteFields(w, criteria);
            if (criteria.Projection is { } projection)
            {
                w.WritePropertyName(ProjectionName);
                Codec.Write(w, projection);
            }

            WriteIfPositive(w, "offset", slice.Offset);
            WriteIfPositive(w, "limit", slice.Limit);
            WriteIfPositive(w, "page", slice.Page);
            WriteIfPositive(w, "pageSize", slice.PageSize);
            w.WriteEndObject();
        });
    }

    public (TCriteria Criteria, Slice Slice) ReadQuery(ReadOnlySpan<byte> body)
    {
        var criteria = NewCriteria();
        int offset = 0, limit = 0, page = 0, pageSize = 0;
        var codec = Codec;
        var reader = RestJson.Start(body);
        RestJson.ReadObject(ref reader, (string name, ref Utf8JsonReader r) =>
        {
            switch (name)
            {
                case ProjectionName:
                    criteria.Projection = r.TokenType == JsonTokenType.Null ? null : codec.Read(ref r);
                    break;
                case "offset":
                    offset = NonNegative(RestJson.ReadInt(ref r, name), name);
                    break;
                case "limit":
                    limit = NonNegative(RestJson.ReadInt(ref r, name), name);
                    break;
                case "page":
                    page = NonNegative(RestJson.ReadInt(ref r, name), name);
                    break;
                case "pageSize":
                    pageSize = NonNegative(RestJson.ReadInt(ref r, name), name);
                    break;
                default:
                    if (!CriteriaCodec.TryReadField(ref r, criteria, name))
                    {
                        r.Skip(); // unknown key: ignored
                    }

                    break;
            }
        });
        return (criteria, new Slice(offset, limit, page, pageSize));
    }

    // :: Fetch by id

    public byte[] WriteFetchById(object key, TEntity? projection)
    {
        ArgumentNullException.ThrowIfNull(key);
        var keyField = Model.Key!;
        return RestJson.Write(w =>
        {
            w.WriteStartObject();
            w.WritePropertyName("id");
            WriteKey(w, keyField, key);
            if (projection is not null)
            {
                w.WritePropertyName(ProjectionName);
                Codec.Write(w, projection);
            }

            w.WriteEndObject();
        });
    }

    /// <summary>The key as an entity holding it, and the projection.</summary>
    public (TEntity KeyHolder, TEntity? Projection) ReadFetchById(ReadOnlySpan<byte> body)
    {
        TEntity? holder = null;
        TEntity? projection = null;
        var codec = Codec;
        var keyName = Model.Key!.Name;
        var reader = RestJson.Start(body);
        RestJson.ReadObject(ref reader, (string name, ref Utf8JsonReader r) =>
        {
            switch (name)
            {
                case "id":
                    holder = ReadKey(ref r, codec, keyName);
                    break;
                case ProjectionName:
                    projection = r.TokenType == JsonTokenType.Null ? null : codec.Read(ref r);
                    break;
                default:
                    r.Skip();
                    break;
            }
        });
        return (holder ?? throw new InvalidRequestException("'id' is required"), projection);
    }

    /// <summary>The key in the text of a route (<c>GET /api/repo/product/7</c>), as an entity holding it.</summary>
    public TEntity KeyFromRoute(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var json = RestJson.Write(w => w.WriteStringValue(text)); // value kinds read numbers from text too
        var reader = RestJson.Start(json);
        return ReadKey(ref reader, Codec, Model.Key!.Name);
    }

    // :: Insert

    public byte[] WriteInsert(TEntity bean, TEntity? projection) => RestJson.Write(w =>
    {
        w.WriteStartObject();
        w.WritePropertyName(EntityName);
        Codec.Write(w, bean, projection is null ? null : ForWriting(projection));
        if (projection is not null)
        {
            w.WritePropertyName(ProjectionName);
            Codec.Write(w, projection);
        }

        w.WriteEndObject();
    });

    /// <summary>Reads the envelope, or the entity alone (the older form the Kotlin server also accepts).</summary>
    public (TEntity Entity, TEntity? Projection) ReadInsert(ReadOnlySpan<byte> body)
    {
        if (!HasTopLevel(body, EntityName))
        {
            var loose = RestJson.Start(body);
            return (Codec.Read(ref loose), null);
        }

        TEntity? entity = null;
        TEntity? projection = null;
        var codec = Codec;
        var reader = RestJson.Start(body);
        RestJson.ReadObject(ref reader, (string name, ref Utf8JsonReader r) =>
        {
            switch (name)
            {
                case EntityName:
                    entity = codec.Read(ref r);
                    break;
                case ProjectionName:
                    projection = r.TokenType == JsonTokenType.Null ? null : codec.Read(ref r);
                    break;
                default:
                    r.Skip();
                    break;
            }
        });
        return (entity!, projection);
    }

    public byte[] WriteInsertResult(bool success, TEntity bean) => RestJson.Write(w =>
    {
        w.WriteStartObject();
        w.WriteBoolean("success", success);
        if (success)
        {
            w.WritePropertyName("id");
            var key = Model.KeyOf(bean);
            if (key is null)
            {
                w.WriteNumberValue(-1);
            }
            else
            {
                WriteKey(w, Model.Key!, key);
            }

            w.WritePropertyName(EntityName);
            Codec.Write(w, bean, Model.OwnFieldsProjection()); // explicit nulls: the client copies what is absent too
        }

        w.WriteEndObject();
    });

    /// <summary>Whether it was inserted, and the entity as stored (when it was).</summary>
    public (bool Success, TEntity? Stored) ReadInsertResult(ReadOnlySpan<byte> body)
    {
        var success = false;
        TEntity? stored = null;
        var codec = Codec;
        var reader = RestJson.Start(body);
        RestJson.ReadObject(ref reader, (string name, ref Utf8JsonReader r) =>
        {
            switch (name)
            {
                case "success":
                    success = r.TokenType == JsonTokenType.True;
                    break;
                case EntityName:
                    stored = r.TokenType == JsonTokenType.Null ? null : codec.Read(ref r);
                    break;
                default:
                    r.Skip();
                    break;
            }
        });
        return (success, stored);
    }

    // :: Update

    /// <summary>Writes the key and the fields <paramref name="fields"/> marks, with explicit nulls.</summary>
    public byte[] WriteUpdate(TEntity bean, TEntity fields) =>
        RestJson.Write(w => Codec.Write(w, bean, ForWriting(fields)));

    /// <summary>The entity, and the projection of the keys that were present (explicit nulls included).</summary>
    public (TEntity Entity, TEntity Presence) ReadUpdate(ReadOnlySpan<byte> body)
    {
        var reader = RestJson.Start(body);
        return Codec.ReadForUpdate(ref reader);
    }

    // :: Results

    public static byte[] WriteSuccess(bool success) => RestJson.Write(w =>
    {
        w.WriteStartObject();
        w.WriteBoolean("success", success);
        w.WriteEndObject();
    });

    public static bool ReadSuccess(ReadOnlySpan<byte> body) => ReadScalar(body, "success", (ref Utf8JsonReader r) => r.TokenType == JsonTokenType.True);

    public static byte[] WriteCount(int count) => RestJson.Write(w =>
    {
        w.WriteStartObject();
        w.WriteNumber("count", count);
        w.WriteEndObject();
    });

    public static int ReadCount(ReadOnlySpan<byte> body) => ReadScalar(body, "count", (ref Utf8JsonReader r) => r.GetInt32());

    public byte[] WriteItems(IReadOnlyList<TEntity> items, TEntity? projection, int? totalItems = null) => RestJson.Write(w =>
    {
        w.WriteStartObject();
        w.WritePropertyName("items");
        Codec.WriteList(w, items, projection);
        if (totalItems is { } total)
        {
            w.WriteNumber("totalItems", total);
        }

        w.WriteEndObject();
    });

    public (IReadOnlyList<TEntity> Items, int TotalItems) ReadItems(ReadOnlySpan<byte> body)
    {
        var items = new List<TEntity>();
        var total = -1;
        var codec = Codec;
        var reader = RestJson.Start(body);
        RestJson.ReadObject(ref reader, (string name, ref Utf8JsonReader r) =>
        {
            switch (name)
            {
                case "items" when r.TokenType == JsonTokenType.StartArray:
                    while (r.Read() && r.TokenType != JsonTokenType.EndArray)
                    {
                        items.Add(codec.Read(ref r));
                    }

                    break;
                case "totalItems":
                    total = r.GetInt32();
                    break;
                default:
                    r.Skip();
                    break;
            }
        });
        return (items, total);
    }

    public byte[] WriteEntity(TEntity entity, TEntity? projection) => RestJson.Write(w => Codec.Write(w, entity, projection));

    public TEntity ReadEntity(ReadOnlySpan<byte> body)
    {
        var reader = RestJson.Start(body);
        return Codec.Read(ref reader);
    }

    // :: Internals

    /// <summary>
    /// The projection to write an entity for a write: the key at every level, scalars as marked; a reference with its
    /// key alone, or with its marked fields when the projection asks for them; a marked collection
    /// with the shape of its children.
    /// </summary>
    private TEntity ForWriting(TEntity projection) => Model.WriteProjection(projection);

    private static void WriteKey(Utf8JsonWriter writer, FieldModel<TEntity> keyField, object key)
    {
        var kind = keyField.ScalarKind ?? throw new InvalidOperationException("The key must be a scalar.");
        kind.WriteObject(writer, key);
    }

    private static TEntity ReadKey(ref Utf8JsonReader reader, JsonEntityCodec<TEntity> codec, string keyName)
    {
        if (reader.TokenType is not (JsonTokenType.Number or JsonTokenType.String))
        {
            throw new InvalidRequestException("'id' must be a number or a text");
        }

        // The model reads the key: wrap the value as {"<key>": value}.
        var raw = reader.HasValueSequence ? System.Buffers.BuffersExtensions.ToArray(reader.ValueSequence) : reader.ValueSpan.ToArray();
        var text = reader.TokenType == JsonTokenType.String ? reader.GetString() : null;
        var json = RestJson.Write(w =>
        {
            w.WriteStartObject();
            w.WritePropertyName(keyName);
            if (text is not null)
            {
                w.WriteStringValue(text);
            }
            else
            {
                w.WriteRawValue(raw);
            }

            w.WriteEndObject();
        });
        var inner = RestJson.Start(json);
        var holder = codec.Read(ref inner);
        if (codec.Model.KeyOf(holder) is null)
        {
            throw new InvalidRequestException("'id' is not a valid key");
        }

        return holder;
    }

    private static bool HasTopLevel(ReadOnlySpan<byte> body, string property)
    {
        var reader = RestJson.Start(body);
        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new InvalidRequestException("a JSON object was expected");
        }

        while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
        {
            if (reader.ValueTextEquals(property))
            {
                return true;
            }

            reader.Read();
            reader.Skip();
        }

        return false;
    }

    private static T ReadScalar<T>(ReadOnlySpan<byte> body, string property, ValueReader<T> read)
    {
        T value = default!;
        var found = false;
        var reader = RestJson.Start(body);
        RestJson.ReadObject(ref reader, (string name, ref Utf8JsonReader r) =>
        {
            if (name == property)
            {
                value = read(ref r);
                found = true;
            }
            else
            {
                r.Skip();
            }
        });
        return found ? value : throw new RemoteCallException($"the answer has no '{property}'");
    }

    private static void WriteIfPositive(Utf8JsonWriter writer, string name, int value)
    {
        if (value > 0)
        {
            writer.WriteNumber(name, value);
        }
    }

    private static int NonNegative(int value, string name) =>
        value >= 0 ? value : throw new InvalidRequestException($"'{name}' cannot be negative");

    private delegate T ValueReader<out T>(ref Utf8JsonReader reader);
}
