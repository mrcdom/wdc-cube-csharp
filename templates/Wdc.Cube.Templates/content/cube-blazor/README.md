# MyCubeApp

A list of tasks with its detail, built with [Cube](https://github.com/mrcdom/wdc-cube-csharp): Blazor Server draws the
screens, and the tasks are kept in PostgreSQL.

## Run it

```bash
docker compose up -d                   # PostgreSQL on localhost:5432, with its data in a volume
dotnet run --project src/MyCubeApp.Blazor
```

The application creates its table and a few tasks the first time it starts. The connection string is in
`src/MyCubeApp.Blazor/appsettings.Development.json`; for another server, set `ConnectionStrings__Tasks` (and
`Database__Schema`, `tasks` by default). `docker compose down -v` drops the database.

## Test it

The tests need a PostgreSQL database where they may create and drop schemas: each test makes one of its own and drops
it at the end. Without `TEST_POSTGRES`, they are skipped and say why.

```bash
export TEST_POSTGRES="Host=localhost;Port=5432;Database=tasks;Username=tasks;Password=tasks"
dotnet test
```

## What is where

| Project | What it has |
|---|---|
| `src/MyCubeApp.Presentation` | The application with no view. `Domain/`: the task, its criteria and the repository contract. `List/` and `Detail/`: one folder per place, with its keys, presenter and scope. `TasksApp`: the application presenter. |
| `src/MyCubeApp.Blazor` | The host (`Program.cs`), the views (`Views/`, one component per scope, and `ViewCatalog`) and the database (`Data/`: the table's mapping, its creation and the first tasks). |
| `tests/MyCubeApp.Tests` | `TasksTests`: the presenters, driven as a view would drive them. `ShellTests`: the page on Blazor's renderer (bUnit). |

Each place is a level of the address: `tasks` is the list, `tasks/task?tk-id=3` is task 3 open beside it. The keys of
a place (`TaskListKeys`, `TaskKeys`) are the only code that knows its names in the address; presenters navigate through
the keys of the destination: `new TaskKeys(App) { TaskId = 3 }.Flip()`.

## Next steps

- **A new field** (a due date): the property and its line in `TaskItem.Model`, a column in `TaskRepository.Mapping`
  and in the table (`TaskStore`), then the scope, the presenter that fills it and the view that draws it.
- **A new screen**: a folder with its keys (a child of the place above), presenter and scope; its place in
  `TasksApp.AllPlaces`; and its view in `ViewCatalog`.
- **Messages**: presenters put a `Message` in the scope (what happened); `Views/Texts.cs` says it in words.
