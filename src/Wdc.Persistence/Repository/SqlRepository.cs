using System.Data.Common;
using Wdc.Domain;

namespace Wdc.Persistence;

/// <summary>
/// A repository built from an <see cref="EntityMapping{TEntity}"/>: every operation of the contract, with no SQL
/// written per entity. Subclass it to add what is particular (a cascade, a guard) or to compose operations.
/// </summary>
/// <remarks>
/// Each fetch is a single statement whose rows are JSON objects shaped by the projection, read back by the same
/// <see cref="JsonEntityCodec{TEntity}"/> the REST layer uses.
/// </remarks>
public class SqlRepository<TEntity, TCriteria, TKey> : IRepository<TEntity, TCriteria, TKey>
    where TEntity : class
    where TCriteria : ICriteria
{
    private const string Alias = "a";

    public SqlRepository(Database database, EntityMapping<TEntity> mapping)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(mapping);
        Database = database;
        Mapping = mapping;
        Codec = new JsonEntityCodec<TEntity>(mapping.Model);
    }

    protected Database Database { get; }

    protected EntityMapping<TEntity> Mapping { get; }

    protected JsonEntityCodec<TEntity> Codec { get; }

    protected EntityModel<TEntity> Model => Mapping.Model;

    public virtual TEntity OwnFieldsProjection() => Model.OwnFieldsProjection();

    // :: Reads

    public virtual Task<int> Count(TCriteria criteria, CancellationToken cancellationToken = default)
    {
        var w = Database.NewWriter();
        w.Append("SELECT COUNT(*) FROM ").Table(Mapping.Table).Append(' ').Identifier(Alias);
        WriteWhere(w, Mapping.Conditions(criteria, Alias));
        var statement = w.ToStatement();
        return Database.Read(async (connection, transaction) =>
        {
            await using var command = Database.Command(connection, transaction, statement);
            return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false),
                System.Globalization.CultureInfo.InvariantCulture);
        }, cancellationToken);
    }

    public virtual Task<IReadOnlyList<TEntity>> Fetch(TCriteria criteria, int offset = 0, int limit = 0,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        ArgumentOutOfRangeException.ThrowIfNegative(limit);
        var projection = ProjectionOf(criteria);
        return Select(projection, Mapping.Conditions(criteria, Alias), criteria, limit == 0 ? null : limit,
            offset == 0 ? null : offset, cancellationToken);
    }

    public virtual async Task<Page<TEntity>> FetchPage(TCriteria criteria, int page, int pageSize,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(page);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pageSize);
        var total = await Count(criteria, cancellationToken).ConfigureAwait(false);
        var items = total == 0 ? [] : await Fetch(criteria, page * pageSize, pageSize, cancellationToken).ConfigureAwait(false);
        return Page<TEntity>.Of(items, page, pageSize, total);
    }

    public virtual async Task<TEntity?> FetchById(TKey id, TEntity? projection = null, CancellationToken cancellationToken = default)
    {
        var key = Model.Key!.ScalarKind!.ToDatabaseObject(id);
        var rows = await Select(projection ?? OwnFieldsProjection(),
            Sql.Compare(Sql.Column(Alias, Mapping.KeyColumn), "=", key), default, 1, null, cancellationToken)
            .ConfigureAwait(false);
        return rows.Count == 0 ? null : rows[0];
    }

    /// <summary>The rows matching <paramref name="where"/>, shaped by <paramref name="projection"/>.</summary>
    protected Task<IReadOnlyList<TEntity>> Select(TEntity projection, ISqlFragment? where, ICriteria? ordering,
        int? limit, int? offset, CancellationToken cancellationToken)
    {
        var w = Database.NewWriter();
        w.Append("SELECT ");
        w.Dialect.WriteJsonAsText(w, json => Mapping.WriteJson(json, Alias, projection));
        w.Append(" FROM ").Table(Mapping.Table).Append(' ').Identifier(Alias);
        WriteWhere(w, where);
        w.Append(" ORDER BY ");
        Mapping.WriteOrderBy(w, Alias, ordering);
        if (limit is not null || offset is not null)
        {
            w.Dialect.WriteSlice(w, limit, offset);
        }

        var statement = w.ToStatement();
        return Database.Read(async (connection, transaction) =>
        {
            var result = new List<TEntity>();
            await using var command = Database.Command(connection, transaction, statement);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                result.Add(Codec.FromJson(reader.GetString(0)));
            }

            return (IReadOnlyList<TEntity>)result;
        }, cancellationToken);
    }

    // :: Writes

    public virtual Task<bool> Insert(TEntity bean, TEntity? projection = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(bean);
        var written = new List<(Binding<TEntity> Binding, object? Value)>();
        foreach (var binding in Mapping.Bindings)
        {
            var field = binding.Field;
            if (field.Kind == FieldKind.Collection || (field.IsKey && Mapping.KeyGenerated))
            {
                continue;
            }

            var wanted = field.IsKey || (projection is null ? field.HasValue(bean) : field.HasValue(projection));
            if (wanted && ColumnValue(binding, bean) is { } value)
            {
                written.Add((binding, value.Value));
            }
        }

        return Database.Write(async (connection, transaction) =>
        {
            var w = Database.NewWriter();
            w.Dialect.WriteInsertReturningKey(w, Mapping.Table,
                [.. written.Select(x => (ColumnOf(x.Binding), x.Value))], Mapping.KeyColumn);
            object? key;
            await using (var command = Database.Command(connection, transaction, w.ToStatement()))
            {
                key = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            }

            // Read the row back: the generated key and what the database filled (defaults, triggers).
            var keyField = Model.Key!;
            var fresh = Model.New();
            keyField.SetValue(fresh, keyField.ScalarKind!.FromDatabaseObject(key));
            var stored = await FetchByKey(keyField.GetValue(fresh), connection, transaction, cancellationToken).ConfigureAwait(false);
            if (stored is null)
            {
                return false;
            }

            foreach (var binding in Mapping.Bindings)
            {
                if (binding.Field.Kind != FieldKind.Collection && !written.Exists(x => ReferenceEquals(x.Binding, binding)))
                {
                    binding.Field.Copy(stored, bean);
                }
            }

            return true;
        }, cancellationToken);
    }

    public virtual Task<bool> Update(TEntity newBean, TEntity? oldBean = null, TEntity? projection = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(newBean);
        var key = Model.Key!.ScalarKind!.ToDatabaseObject(Model.KeyOf(newBean))
                  ?? throw new InvalidRequestException($"update of {Model.Name} without its key");
        var marks = projection ?? (oldBean is null ? OwnFieldsProjection() : Model.ChangedFields(newBean, oldBean));

        var sets = new List<(string Column, object? Value)>();
        foreach (var binding in Mapping.Bindings)
        {
            var field = binding.Field;
            if (field.IsKey || field.Kind == FieldKind.Collection || !field.HasValue(marks))
            {
                continue;
            }

            if (oldBean is not null && projection is not null && !field.Differs(newBean, oldBean))
            {
                continue; // marked but unchanged
            }

            sets.Add((ColumnOf(binding), ColumnValue(binding, newBean)?.Value));
        }

        if (sets.Count == 0)
        {
            return Task.FromResult(false);
        }

        var w = Database.NewWriter();
        w.Append("UPDATE ").Table(Mapping.Table).Append(" SET ")
            .Join(", ", sets, (ww, s) => ww.Identifier(s.Column).Append(" = ").Parameter(s.Value))
            .Append(" WHERE ").Identifier(Mapping.KeyColumn).Append(" = ").Parameter(key);
        var statement = w.ToStatement();
        return Database.Write(async (connection, transaction) =>
        {
            await using var command = Database.Command(connection, transaction, statement);
            return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) > 0;
        }, cancellationToken);
    }

    public virtual Task<int> Delete(TCriteria criteria, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(criteria);
        if (!criteria.HasFilter)
        {
            throw new InvalidRequestException($"delete of {Model.Name} without a filter is refused");
        }

        var w = Database.NewWriter();
        w.Dialect.WriteDeleteFrom(w, Mapping.Table, Alias);
        WriteWhere(w, Mapping.Conditions(criteria, Alias));
        var statement = w.ToStatement();
        return Database.Write(async (connection, transaction) =>
        {
            await using var command = Database.Command(connection, transaction, statement);
            return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }, cancellationToken);
    }

    // :: Helpers

    /// <summary>The criteria's projection, or the default. The key always comes (the mapping adds it).</summary>
    protected TEntity ProjectionOf(ICriteria criteria) =>
        (criteria as IProjectionCarrier<TEntity>)?.Projection ?? OwnFieldsProjection();

    private static void WriteWhere(SqlWriter w, ISqlFragment? where)
    {
        if (where is not null)
        {
            w.Append(" WHERE ");
            where.Render(w);
        }
    }

    private async Task<TEntity?> FetchByKey(object? key, DbConnection connection, DbTransaction? transaction,
        CancellationToken cancellationToken)
    {
        var projection = OwnFieldsProjection();
        var w = Database.NewWriter();
        w.Append("SELECT ");
        w.Dialect.WriteJsonAsText(w, json => Mapping.WriteJson(json, Alias, projection));
        w.Append(" FROM ").Table(Mapping.Table).Append(' ').Identifier(Alias).Append(" WHERE ")
            .Column(Alias, Mapping.KeyColumn).Append(" = ").Parameter(Model.Key!.ScalarKind!.ToDatabaseObject(key));
        await using var command = Database.Command(connection, transaction, w.ToStatement());
        var json = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) as string;
        return json is null ? null : Codec.FromJson(json);
    }

    private static string ColumnOf(Binding<TEntity> binding) => binding switch
    {
        ScalarBinding<TEntity> scalar => scalar.Column,
        IReferenceColumn<TEntity> reference => reference.ForeignKeyColumn,
        _ => throw new InvalidOperationException($"{binding.Field.Name} has no column of its own."),
    };

    /// <summary>The value a binding writes to its column (a reference writes its child's key); null when not applicable.</summary>
    private static Wrapped? ColumnValue(Binding<TEntity> binding, TEntity bean) => binding switch
    {
        ScalarBinding<TEntity> scalar => new(scalar.Kind.ToDatabaseObject(scalar.Field.GetValue(bean)) ?? TypedNull.Of(scalar.Kind)),
        IReferenceColumn<TEntity> reference => new(reference.ChildKeyOf(bean) ?? TypedNull.Of(reference.ChildKeyKind)),
        _ => null,
    };

    private sealed record Wrapped(object? Value);
}
