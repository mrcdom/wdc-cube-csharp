using System.Text.Json.Nodes;
using Microsoft.OpenApi;
using Wdc.Domain;

namespace Wdc.Rest.Server;

/// <summary>
/// The schemas of the wire, read from the entity models and criteria (no reflection), registered once each in the
/// document's components: the entity, its projection, its criteria, its query and the envelopes of the answers.
/// </summary>
internal sealed class RestOpenApiSchemas
{
    private readonly OpenApiDocument _document;
    private readonly Dictionary<IEntityModel, string> _names = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<IValueKind, string> _criterionNames = new(ReferenceEqualityComparer.Instance);

    public RestOpenApiSchemas(OpenApiDocument document)
    {
        _document = document;
        _document.Components ??= new OpenApiComponents();
        _document.Components.Schemas ??= new Dictionary<string, IOpenApiSchema>(StringComparer.Ordinal);
    }

    private IDictionary<string, IOpenApiSchema> Components => _document.Components!.Schemas!;

    // :: Shared

    public IOpenApiSchema Error() => Shared("CubeError", () => Object(
        "Every error answer of the routes. The message is meant for people and logs: do not parse it.",
        ("error", String())));

    public IOpenApiSchema Count() => Shared("CubeCount", () => Object("A number of rows.", ("count", Int32())));

    public IOpenApiSchema Success() => Shared("CubeSuccess", () => Object(
        "Whether the repository wrote something.", ("success", new OpenApiSchema { Type = JsonSchemaType.Boolean })));

    public IOpenApiSchema TransactionBegun() => Shared("CubeTransactionBegun", () => Object(
        "A remote transaction begun: send its id as X-Tx-Id on every request of the unit of work.",
        ("txId", new OpenApiSchema { Type = JsonSchemaType.String, Description = "Opaque." })));

    public IOpenApiSchema TransactionStatus() => Shared("CubeTransactionStatus", () => Object(
        "The state of a remote transaction.",
        ("status", Enum(["open", "committed", "rolledback", "unknown"]))));

    // :: One entity

    /// <summary>The entity, as results carry it and writes send it.</summary>
    public IOpenApiSchema Entity(IEntityModel model) => Of(model, string.Empty, () =>
    {
        var schema = Object($"{model.Name}. A field may be absent; one marked by the projection but empty comes as null.");
        foreach (var field in model.Fields)
        {
            schema.Properties![field.Name] = field.Kind switch
            {
                FieldKind.Scalar => Nullable(Value(field.ScalarKind!), field.IsKey ? "The key." : null),
                FieldKind.Reference => Nullable(Entity(field.ChildModel!), null),
                _ => Nullable(new OpenApiSchema { Type = JsonSchemaType.Array, Items = Entity(field.ChildModel!) }, null),
            };
        }

        return schema;
    });

    /// <summary>A projection: the same shape, where a marked field holds any value of its kind.</summary>
    public IOpenApiSchema Projection(IEntityModel model) => Of(model, "Projection", () =>
    {
        var schema = Object($"Which fields of {model.Name} to read or to write: a field holding any value of its kind is "
            + "marked (only its presence counts). The key always comes back. Absent or null: the repository's default.");
        foreach (var field in model.Fields)
        {
            schema.Properties![field.Name] = field.Kind switch
            {
                FieldKind.Scalar => Value(field.ScalarKind!),
                FieldKind.Reference => Projection(field.ChildModel!),
                _ => CollectionProjection(field),
            };
        }

        return schema;
    });

    /// <summary>The criteria: one field object per criterion, and the ordering.</summary>
    public IOpenApiSchema Criteria(IEntityModel model, Func<ICriteria> newCriteria) => Of(model, "Criteria", () =>
    {
        var criteria = newCriteria();
        var schema = Object($"What filters {model.Name}: fields combine with AND; a field that asks nothing is left out.");
        foreach (var criterion in criteria.Criterions)
        {
            var loose = Nullable(Value(criterion.ValueKind), "A bare value: equality (null asks nothing).");
            schema.Properties![criterion.Name] = new OpenApiSchema { OneOf = [Criterion(criterion.ValueKind), loose] };
        }

        schema.Properties![CriteriaCodec.OrderByProperty] = OrderBy(model, criteria.AcceptedOrderBy);
        return schema;
    });

    /// <summary>The body of count, fetch, fetch-page and delete: the criteria, the projection and the slice.</summary>
    public IOpenApiSchema Query(IEntityModel model, Func<ICriteria> newCriteria) => Of(model, "Query", () => new OpenApiSchema
    {
        Description = $"A query on {model.Name}. Keys that are neither criteria fields nor reserved are ignored.",
        AllOf =
        [
            Criteria(model, newCriteria),
            Object(null,
                ("projection", Nullable(Projection(model), "Read by fetch and fetch-page.")),
                ("offset", Slice("Rows to skip, for fetch; 0: none.")),
                ("limit", Slice("Most rows, for fetch; 0: no limit (the server's maximum still holds).")),
                ("page", Slice("The page, from 0, for fetch-page.")),
                ("pageSize", Slice("Rows per page, for fetch-page: from 1 to the server's maximum."))),
        ],
    });

    public IOpenApiSchema Items(IEntityModel model) => Of(model, "Items", () => Required(Object(
        $"The {model.Name} rows found.", ("items", Array(Entity(model)))), "items"));

    public IOpenApiSchema Page(IEntityModel model) => Of(model, "Page", () => Required(Object(
        $"One page of {model.Name} rows.",
        ("items", Array(Entity(model))),
        ("totalItems", Int32("Rows that match, regardless of the page."))), "items", "totalItems"));

    public IOpenApiSchema FetchById(IEntityModel model) => Of(model, "FetchById", () => Required(Object(
        $"Which {model.Name} to read, and which of its fields.",
        ("id", Key(model)),
        ("projection", Nullable(Projection(model), null))), "id"));

    /// <summary>The body of an insert: the envelope with a projection, or the entity alone.</summary>
    public IOpenApiSchema Insert(IEntityModel model) => Of(model, "Insert", () => new OpenApiSchema
    {
        Description = $"The {model.Name} to insert. A body with a top-level 'entity' key is the envelope; any other is "
            + "the entity itself, written without a projection.",
        OneOf =
        [
            Required(Object(null,
                ("entity", Entity(model)),
                ("projection", Nullable(Projection(model), "Which fields to write."))), "entity"),
            Entity(model),
        ],
    });

    public IOpenApiSchema Inserted(IEntityModel model) => Of(model, "Inserted", () => Required(Object(
        $"The {model.Name} inserted, as stored. Without success, neither id nor entity.",
        ("success", new OpenApiSchema { Type = JsonSchemaType.Boolean }),
        ("id", Key(model, "The stored key; -1 when there is none.")),
        ("entity", Entity(model))), "success"));

    /// <summary>The key's value, for a body or the path.</summary>
    public IOpenApiSchema Key(IEntityModel model, string? description = null)
    {
        var schema = model.Key?.ScalarKind is { } kind ? Value(kind) : new OpenApiSchema();
        schema.Description = description;
        return schema;
    }

    // :: Criteria parts

    /// <summary>The field object of a criterion of one value kind: <c>{"or":true,"p":[{"o":"EQ","v":[…]}]}</c>.</summary>
    private IOpenApiSchema Criterion(IValueKind kind)
    {
        if (!_criterionNames.TryGetValue(kind, out var name))
        {
            name = Free(Pascal(kind.Name) + "Criterion");
            _criterionNames[kind] = name;
            Components[name] = Object($"Requests on a field of kind {kind.Name}.",
                ("or", new OpenApiSchema
                {
                    Type = JsonSchemaType.Boolean,
                    Description = "true: the requests combine with OR. Absent: AND.",
                }),
                ("p", new OpenApiSchema
                {
                    Type = JsonSchemaType.Array,
                    Description = "The requests. Empty: asks nothing.",
                    Items = Required(Object(null,
                        ("o", Enum(Operators(kind), "The operator. An unknown one is dropped.")),
                        ("v", new OpenApiSchema
                        {
                            Type = JsonSchemaType.Array,
                            Description = "The operands: one, two for BETWEEN, any number for IN, none for IS_NULL and IS_NOT_NULL.",
                            Items = Nullable(Value(kind), null),
                        })), "o"),
                }));
        }

        return Ref(name);
    }

    private IOpenApiSchema OrderBy(IEntityModel model, IReadOnlyList<string> accepted) => Of(model, "OrderBy", () =>
    {
        if (accepted.Count == 0)
        {
            return new OpenApiSchema { Type = JsonSchemaType.Null, Description = $"{model.Name} has no ordering to choose." };
        }

        var schema = Enum(accepted, "The ordering; null or absent: the repository's default. An unknown name is refused.");
        return new OpenApiSchema { Description = schema.Description, AnyOf = [schema, new OpenApiSchema { Type = JsonSchemaType.Null }] };
    });

    private static IEnumerable<string> Operators(IValueKind kind)
    {
        var text = ReferenceEquals(kind, ValueKinds.String);
        foreach (var op in System.Enum.GetValues<Operator>())
        {
            if (text || op is not (Operator.Like or Operator.ILike))
            {
                yield return op.WireName();
            }
        }
    }

    private OpenApiSchema CollectionProjection(IFieldModel field)
    {
        var child = field.ChildModel!;
        var envelope = Object("Filters, orders or slices the children, within each parent.",
            ("shape", Projection(child)),
            ("limit", Slice("Most children per parent; 0: no limit.")),
            ("offset", Slice("Children to skip per parent.")));
        if (field.NewCriteria is { } newCriteria)
        {
            envelope.Properties!["where"] = Criteria(child, newCriteria);
        }

        return new OpenApiSchema
        {
            OneOf =
            [
                new OpenApiSchema { Type = JsonSchemaType.Array, MaxItems = 1, Items = Projection(child), Description = "One shape." },
                envelope,
            ],
        };
    }

    // :: Values

    /// <summary>A value of a kind, as it travels.</summary>
    private static OpenApiSchema Value(IValueKind kind)
    {
        if (kind.Choices is { } choices)
        {
            return Enum(choices);
        }

        return kind switch
        {
            _ when ReferenceEquals(kind, ValueKinds.Int32) => new OpenApiSchema { Type = JsonSchemaType.Integer, Format = "int32" },
            _ when ReferenceEquals(kind, ValueKinds.Int64) => new OpenApiSchema { Type = JsonSchemaType.Integer, Format = "int64" },
            _ when ReferenceEquals(kind, ValueKinds.Double) => new OpenApiSchema { Type = JsonSchemaType.Number, Format = "double" },
            _ when ReferenceEquals(kind, ValueKinds.Decimal) => new OpenApiSchema { Type = JsonSchemaType.Number, Format = "decimal" },
            _ when ReferenceEquals(kind, ValueKinds.String) => String(),
            _ when ReferenceEquals(kind, ValueKinds.Boolean) => new OpenApiSchema { Type = JsonSchemaType.Boolean },
            _ when ReferenceEquals(kind, ValueKinds.Instant) => new OpenApiSchema
            {
                Type = JsonSchemaType.String,
                Format = "date-time",
                Description = "ISO-8601; written in UTC with Z.",
            },
            _ when ReferenceEquals(kind, ValueKinds.Date) => new OpenApiSchema { Type = JsonSchemaType.String, Format = "date" },
            _ when ReferenceEquals(kind, ValueKinds.Uuid) => new OpenApiSchema { Type = JsonSchemaType.String, Format = "uuid" },
            _ when ReferenceEquals(kind, ValueKinds.Bytes) => new OpenApiSchema
            {
                Type = JsonSchemaType.String,
                Format = "byte",
                Description = "Base64.",
            },
            _ => new OpenApiSchema { Description = $"A value of kind {kind.Name}." },
        };
    }

    /// <summary>
    /// A schema that also admits null: the type gains null, or, for a reference or a list of names, an alternative.
    /// </summary>
    private static IOpenApiSchema Nullable(IOpenApiSchema schema, string? description)
    {
        if (schema is OpenApiSchema { Type: { } type, Enum: null or { Count: 0 } } own)
        {
            own.Type = type | JsonSchemaType.Null;
            own.Description = description ?? own.Description;
            return own;
        }

        return new OpenApiSchema { Description = description, AnyOf = [schema, new OpenApiSchema { Type = JsonSchemaType.Null }] };
    }

    private static OpenApiSchema Enum(IEnumerable<string> names, string? description = null) => new()
    {
        Type = JsonSchemaType.String,
        Description = description,
        Enum = [.. names.Select(n => (JsonNode)JsonValue.Create(n))],
    };

    private static OpenApiSchema String() => new() { Type = JsonSchemaType.String };

    private static OpenApiSchema Int32(string? description = null) =>
        new() { Type = JsonSchemaType.Integer, Format = "int32", Description = description };

    private static OpenApiSchema Slice(string description) =>
        new() { Type = JsonSchemaType.Integer, Format = "int32", Minimum = "0", Description = description };

    private static OpenApiSchema Array(IOpenApiSchema items) => new() { Type = JsonSchemaType.Array, Items = items };

    private static OpenApiSchema Object(string? description, params (string Name, IOpenApiSchema Schema)[] properties)
    {
        var schema = new OpenApiSchema
        {
            Type = JsonSchemaType.Object,
            Description = description,
            Properties = new Dictionary<string, IOpenApiSchema>(StringComparer.Ordinal),
        };
        foreach (var (name, property) in properties)
        {
            schema.Properties[name] = property;
        }

        return schema;
    }

    private static OpenApiSchema Required(OpenApiSchema schema, params string[] names)
    {
        schema.Required = new HashSet<string>(names, StringComparer.Ordinal);
        return schema;
    }

    // :: Components

    /// <summary>The component of one model's schema, built on first use (a reference first, so cycles end).</summary>
    private IOpenApiSchema Of(IEntityModel model, string suffix, Func<IOpenApiSchema> build)
    {
        if (!_names.TryGetValue(model, out var baseName))
        {
            baseName = Free(model.Name);
            _names[model] = baseName;
        }

        var name = baseName + suffix;
        if (!Components.ContainsKey(name))
        {
            Components[name] = new OpenApiSchema(); // holds the name while the schema refers to itself
            Components[name] = build();
        }

        return Ref(name);
    }

    private IOpenApiSchema Shared(string name, Func<IOpenApiSchema> build)
    {
        if (!Components.ContainsKey(name))
        {
            Components[name] = build();
        }

        return Ref(name);
    }

    private OpenApiSchemaReference Ref(string name) => new(name, _document);

    // A name the host's own schemas do not use: the model's own, or with a number after it.
    private string Free(string name)
    {
        var candidate = name;
        for (var n = 2; Components.ContainsKey(candidate) || _names.ContainsValue(candidate); n++)
        {
            candidate = name + n.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        return candidate;
    }

    private static string Pascal(string name) => name.Length == 0 ? name : char.ToUpperInvariant(name[0]) + name[1..];
}
