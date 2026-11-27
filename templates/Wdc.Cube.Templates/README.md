# Wdc.Cube.Templates

`dotnet new` templates for [Cube](https://github.com/mrcdom/wdc-cube-csharp).

```bash
dotnet new install Wdc.Cube.Templates
dotnet new cube-blazor -n MyApp
cd MyApp
docker compose up -d
dotnet run --project src/MyApp.Blazor
```

## cube-blazor

A list of tasks with its detail, drawn by Blazor Server and kept in PostgreSQL: places and their keys, presenters and
scopes in a project with no view, the views and the database in the host, and tests of the presenters (with no view)
and of the page (bUnit). The `README.md` of the new project says how to run and test it.

| Option | Default | |
|---|---|---|
| `-n`, `--name` | the folder's name | The name of the solution, its projects and its namespaces. |
| `--Framework` | `net10.0` | `net10.0` or `net11.0`. |
