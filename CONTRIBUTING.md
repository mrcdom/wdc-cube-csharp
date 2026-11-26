# Contributing

Thanks for your interest. This guide covers building, testing and the conventions the code follows. For anything
that changes the structure (a new package, a change to a contract, to the wire or to navigation), open an issue first
to discuss it.

## Build and test

- .NET 11 SDK (`global.json`). The libraries target `net10.0` and `net11.0`; both are built and tested.
- `dotnet build wdc-cube.slnx` — warnings are errors.
- `dotnet format wdc-cube.slnx` — formatting, import order and names follow `.editorconfig`; CI runs it with
  `--verify-no-changes`.
- `dotnet test --solution wdc-cube.slnx` — the tests run on the Microsoft Testing Platform (xUnit v3). To run one
  project or one class:

  ```bash
  dotnet test --project tests/Wdc.Cube.Tests/Wdc.Cube.Tests.csproj -f net10.0 --filter-class "*Navigation*"
  ```

### Databases

The persistence and Shopping tests run against real databases, each test run in a throwaway schema. Without a
connection string they are skipped, with the reason.

```bash
source scripts/test-env.sh
```

- **PostgreSQL**: a local server with a dedicated database. The default is `Host=localhost;Database=wdc_cube_test`,
  as your OS user without a password; set `WDC_TEST_POSTGRES` for anything else.
- **SQL Server**: set `WDC_TEST_SQLSERVER`, or `WDC_TEST_SQLSERVER_HOST` (and optionally `_USER`, `_DATABASE`) with
  the password in the macOS Keychain (see the script).
- Values for your machine go in `scripts/test-env.local.sh`, which git ignores and the script reads first.
- CI (`.github/workflows/ci.yml`) runs the whole suite on every pull request, with PostgreSQL and SQL Server in
  throwaway containers.

### Browser and desktop

The Blazor views are tested with bUnit and the Avalonia views headless; both run in the normal test run. When you
change something only a real browser shows (JavaScript interop, reload, Back/Forward, layout), also run the sample and
check it by hand.

## Conventions

- **The presentation does not know the view technology**: no Blazor or Avalonia types in presenters and scopes.
- **No runtime reflection in the core and the domain**: the libraries stay trimming- and AOT-compatible.
- **No `ConfigureAwait(false)` in presentation code**: awaits must come back to the application's dispatcher.
- **One public type per file**, named after the type. Exceptions: types with the same name and different generic
  arity (`Criteria` and `Criteria<…>`); small `internal` helpers next to their only user; and the scopes of one screen,
  kept together in the file of the screen's root scope with the value types they expose.
- **Comments explain the code's logic** — why it does what it does — not the history of how it got there.
- **Package versions** live only in `Directory.Packages.props`.
- **The public API is listed.** Each library in `src/` lists its public API in `PublicAPI.Shipped.txt` (released)
  and `PublicAPI.Unshipped.txt` (since the last release). Adding, changing or removing a public member without
  updating `PublicAPI.Unshipped.txt` breaks the build (RS0016/RS0017); the IDE's code fix writes the line. On a
  release, the unshipped lines move to the shipped file.
- **Each change that a user of the packages can notice gets a line in [`CHANGELOG.md`](CHANGELOG.md)**, under
  *Unreleased*, saying whether it breaks anything and what to do.
- **Tests come with the change.** Behavior visible to an application is tested through the presentation (without a
  view) and, when data is involved, both in process and through HTTP, as the Shopping tests do.

## Documentation

The documentation is in `docs/`. Code blocks preceded by `<!-- snippet: name -->` come from regions of
`samples/GettingStarted` marked `// <snippet:name>` … `// </snippet:name>`, and a test fails when they differ. After
changing the sample, rewrite the blocks with:

```bash
python3 scripts/update-doc-snippets.py
```

## Security

Report vulnerabilities privately, as [SECURITY.md](SECURITY.md) describes, not in an issue.

## License

By contributing, you agree that your contributions are licensed under the [MIT License](LICENSE).
