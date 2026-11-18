using System.Data.Common;
using Wdc.Domain;

namespace Wdc.Persistence;

/// <summary>
/// A repository built from an <see cref="EntityMapping{TEntity}"/>: every operation of the contract, with no SQL
/// written per entity. Subclass it to add what is particular (a validation, a guard) or to compose operations.
/// </summary>
/// <remarks>
/// <para>Each fetch is a single statement whose rows are JSON objects shaped by the projection, read back by the
/// same <see cref="JsonEntityCodec{TEntity}"/> the REST layer uses.</para>
/// <para><b>Writes cascade as the projection asks</b> (decision D-23), in one transaction: a referenced entity marked
/// beyond its key is inserted or updated; a marked collection is inserted with its parent, and on update it is the
/// full state (new children inserted, known ones updated, missing ones deleted). A collection declared
/// <c>cascadeDelete</c> goes with its parent on delete. Each child is written by its own repository, which must exist
/// on the same <see cref="Persistence.Database"/>.</para>
/// </remarks>
public class SqlRepository<TEntity, TCriteria, TKey> : IRepository<TEntity, TCriteria, TKey>, IEntityStore<TEntity>
    where TEntity : class
    where TCriteria : ICriteria
{
    private const string Alias = "a";

    public SqlRepository(Database database, EntityMapping<TEntity> mapping)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(mapping);
        Database = database;
        TableMapping = mapping;
        Codec = new JsonEntityCodec<TEntity>(mapping.Model);
        database.Register(mapping, this); // the repository a cascade writes this entity through
    }

    protected Database Database { get; }

    protected EntityMapping<TEntity> TableMapping { get; }

    protected JsonEntityCodec<TEntity> Codec { get; }

    protected EntityModel<TEntity> Model => TableMapping.Model;

    public virtual TEntity OwnFieldsProjection() => Model.OwnFieldsProjection();

    // :: Reads

    public virtual Task<int> Count(TCriteria criteria, CancellationToken cancellationToken = default)
    {
        var w = Database.NewWriter();
        w.Append("SELECT COUNT(*) FROM ").Table(TableMapping.Table).Append(' ').Identifier(Alias);
        WriteWhere(w, TableMapping.Conditions(criteria, Alias));
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
        return Select(projection, TableMapping.Conditions(criteria, Alias), criteria, limit == 0 ? null : limit,
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
            Sql.Compare(Sql.Column(Alias, TableMapping.KeyColumn), "=", key), default, 1, null, cancellationToken)
            .ConfigureAwait(false);
        return rows.Count == 0 ? null : rows[0];
    }

    /// <summary>The rows matching <paramref name="where"/>, shaped by <paramref name="projection"/>.</summary>
    protected Task<IReadOnlyList<TEntity>> Select(TEntity projection, ISqlFragment? where, ICriteria? ordering,
        int? limit, int? offset, CancellationToken cancellationToken)
    {
        var w = Database.NewWriter();
        w.Append("SELECT ");
        w.Dialect.WriteJsonAsText(w, json => TableMapping.WriteJson(json, Alias, projection));
        w.Append(" FROM ").Table(TableMapping.Table).Append(' ').Identifier(Alias);
        WriteWhere(w, where);
        w.Append(" ORDER BY ");
        TableMapping.WriteOrderBy(w, Alias, ordering);
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
        return Database.Required(async _ =>
        {
            if (projection is not null)
            {
                foreach (var binding in TableMapping.Bindings)
                {
                    await binding.BeforeWrite(Database, bean, projection, cancellationToken).ConfigureAwait(false);
                }
            }

            if (!await InsertOwnRow(bean, projection, cancellationToken).ConfigureAwait(false))
            {
                return false;
            }

            foreach (var binding in TableMapping.Bindings)
            {
                await binding.AfterInsert(Database, bean, projection, cancellationToken).ConfigureAwait(false);
            }

            return true;
        }, cancellationToken);
    }

    private Task<bool> InsertOwnRow(TEntity bean, TEntity? projection, CancellationToken cancellationToken)
    {
        var written = new List<(Binding<TEntity> Binding, object? Value)>();
        foreach (var binding in TableMapping.Bindings)
        {
            var field = binding.Field;
            if (field.Kind == FieldKind.Collection || (field.IsKey && TableMapping.KeyGenerated))
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
            w.Dialect.WriteInsertReturningKey(w, TableMapping.Table,
                [.. written.Select(x => (ColumnOf(x.Binding), x.Value))], TableMapping.KeyColumn);
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

            foreach (var binding in TableMapping.Bindings)
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
        var keyValue = Model.KeyOf(newBean) ?? throw new InvalidRequestException($"update of {Model.Name} without its key");
        return Database.Required(async _ =>
        {
            var changed = false;
            if (projection is not null)
            {
                foreach (var binding in TableMapping.Bindings)
                {
                    changed |= await binding.BeforeWrite(Database, newBean, projection, cancellationToken).ConfigureAwait(false);
                }
            }

            changed |= await UpdateOwnRow(newBean, oldBean, projection, cancellationToken).ConfigureAwait(false);

            if (projection is not null)
            {
                var key = Model.Key!.ScalarKind!.ToDatabaseObject(keyValue)!;
                foreach (var binding in TableMapping.Bindings)
                {
                    changed |= await binding.AfterUpdate(Database, newBean, projection, key, cancellationToken).ConfigureAwait(false);
                }
            }

            return changed;
        }, cancellationToken);
    }

    private Task<bool> UpdateOwnRow(TEntity newBean, TEntity? oldBean, TEntity? projection, CancellationToken cancellationToken)
    {
        var key = Model.Key!.ScalarKind!.ToDatabaseObject(Model.KeyOf(newBean));
        var marks = projection ?? (oldBean is null ? OwnFieldsProjection() : Model.ChangedFields(newBean, oldBean));

        var sets = new List<(string Column, object? Value)>();
        foreach (var binding in TableMapping.Bindings)
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
        w.Append("UPDATE ").Table(TableMapping.Table).Append(" SET ")
            .Join(", ", sets, (ww, s) => ww.Identifier(s.Column).Append(" = ").Parameter(s.Value))
            .Append(" WHERE ").Identifier(TableMapping.KeyColumn).Append(" = ").Parameter(key);
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

        if (!TableMapping.Bindings.Any(b => b.CascadesDelete))
        {
            var w = Database.NewWriter();
            w.Dialect.WriteDeleteFrom(w, TableMapping.Table, Alias);
            WriteWhere(w, TableMapping.Conditions(criteria, Alias));
            var statement = w.ToStatement();
            return Database.Write(async (connection, transaction) =>
            {
                await using var command = Database.Command(connection, transaction, statement);
                return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }, cancellationToken);
        }

        // The keys first (the criteria may depend on the children, which go before the parents), then the cascade.
        return Database.Required(async _ =>
        {
            var keys = await SelectKeys(TableMapping.Conditions(criteria, Alias), cancellationToken).ConfigureAwait(false);
            if (keys.Count > 0)
            {
                await DeleteByKeys(keys, cancellationToken).ConfigureAwait(false);
            }

            return keys.Count;
        }, cancellationToken);
    }

    // :: The child side of a cascade (IEntityStore)

    Task<bool> IEntityStore<TEntity>.InsertRow(TEntity bean, TEntity? projection, CancellationToken cancellationToken) =>
        Insert(bean, projection, cancellationToken);

    Task<bool> IEntityStore<TEntity>.UpdateRow(TEntity bean, TEntity projection, CancellationToken cancellationToken) =>
        Update(bean, null, projection, cancellationToken);

    Task IEntityStore<TEntity>.DeleteByKeys(IReadOnlyList<object> keys, CancellationToken cancellationToken) =>
        DeleteByKeys(keys, cancellationToken);

    Task<IReadOnlyList<object>> IEntityStore<TEntity>.KeysWhere(string column, IReadOnlyList<object> values,
        CancellationToken cancellationToken) =>
        SelectKeys(Sql.In(Sql.Column(Alias, column), values), cancellationToken);

    private Task DeleteByKeys(IReadOnlyList<object> keys, CancellationToken cancellationToken) =>
        Database.Required(async _ =>
        {
            foreach (var binding in TableMapping.Bindings)
            {
                await binding.BeforeDelete(Database, keys, cancellationToken).ConfigureAwait(false);
            }

            var kind = Model.Key!.ScalarKind!;
            var w = Database.NewWriter();
            w.Dialect.WriteDeleteFrom(w, TableMapping.Table, Alias);
            WriteWhere(w, Sql.In(Sql.Column(Alias, TableMapping.KeyColumn), [.. keys.Select(k => kind.ToDatabaseObject(k))]));
            var statement = w.ToStatement();
            return await Database.Write(async (connection, transaction) =>
            {
                await using var command = Database.Command(connection, transaction, statement);
                return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }, cancellationToken).ConfigureAwait(false);
        }, cancellationToken);

    /// <summary>The keys (model form) of the rows matching <paramref name="where"/>.</summary>
    private Task<IReadOnlyList<object>> SelectKeys(ISqlFragment? where, CancellationToken cancellationToken)
    {
        var w = Database.NewWriter();
        w.Append("SELECT ").Column(Alias, TableMapping.KeyColumn).Append(" FROM ").Table(TableMapping.Table).Append(' ').Identifier(Alias);
        WriteWhere(w, where);
        var statement = w.ToStatement();
        var kind = Model.Key!.ScalarKind!;
        return Database.Read(async (connection, transaction) =>
        {
            var keys = new List<object>();
            await using var command = Database.Command(connection, transaction, statement);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                keys.Add(kind.FromDatabaseObject(reader.GetValue(0))!);
            }

            return (IReadOnlyList<object>)keys;
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
        w.Dialect.WriteJsonAsText(w, json => TableMapping.WriteJson(json, Alias, projection));
        w.Append(" FROM ").Table(TableMapping.Table).Append(' ').Identifier(Alias).Append(" WHERE ")
            .Column(Alias, TableMapping.KeyColumn).Append(" = ").Parameter(Model.Key!.ScalarKind!.ToDatabaseObject(key));
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
