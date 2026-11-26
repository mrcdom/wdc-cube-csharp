# 0011. Supported databases: PostgreSQL and SQL Server

## Status

Accepted (2026-11-18).

## Context

The SQL engine ([0010](0010-own-sql-ast-over-ado-net.md)) relies on the database to build JSON rows, aggregate ordered
JSON arrays and return generated keys from an insert. Each database supported is a dialect to write, test and keep
working. The first versions need the two most common servers in .NET shops.

## Decision

- Support **PostgreSQL** (`Wdc.Persistence.PostgreSql`, over Npgsql) and **SQL Server** (`Wdc.Persistence.SqlServer`,
  over Microsoft.Data.SqlClient). SQLite and MySQL are out for now.
- Each package provides a dialect (`ISqlDialect`) and a `CreateDatabase` factory.
- PostgreSQL: rows with `jsonb_build_object`, in blocks of at most 50 pairs joined with `||` (the function takes at
  most 100 arguments); collections with `jsonb_agg` ordered inside the aggregate; instants in `timestamptz`.
- SQL Server: the dialect targets **SQL Server 2025**, whose `JSON_OBJECT` and `JSON_ARRAYAGG … ORDER BY` are enough to
  build the projection without `FOR JSON`; nested JSON is wrapped in `JSON_QUERY`; instants in `datetimeoffset`;
  bytes as Base64.

## Alternatives considered

- **SQLite and MySQL as well**: left out for now, to keep the dialect work to two databases; each can be added later as
  its own package.
- **`FOR JSON PATH` on SQL Server**, to reach versions before `JSON_OBJECT`/`JSON_ARRAYAGG`: not needed once the
  target version has the JSON constructors with ordered aggregation.

## Consequences

- With no embedded database, repository tests need real servers. They read connection strings from the environment
  and are skipped, with the reason, when none is set; tests that do not touch a database always run.
- Every repository contract test runs on both databases, locally and through HTTP.
- Read isolation is the database's: SQL Server's default read committed blocks readers on uncommitted rows unless
  `READ_COMMITTED_SNAPSHOT` is on.

See [Persistence](../concepts/persistence.md#dialects) and [CONTRIBUTING](https://github.com/mrcdom/wdc-cube-csharp/blob/main/CONTRIBUTING.md).
