# 0002. Target frameworks: `net10.0` and `net11.0`, C# 14

## Status

Accepted (2026-11-18).

## Context

At the time of the decision, .NET 10 was the long-term support release (36 months) and .NET 11 had just shipped as a
standard-term release (24 months). A framework distributed through NuGet must serve applications that stay on the LTS,
while being tested on the newest runtime. Building `net11.0` with its default language version would allow C# 15
features to creep into code that must also compile for `net10.0`.

## Decision

- Every project targets `net10.0;net11.0`, set once in `Directory.Build.props`.
- The build uses the .NET 11 SDK, pinned in `global.json` (`rollForward: latestFeature`).
- `LangVersion` is fixed at **14**, so the same source compiles the same way for both targets. A newer language
  feature is adopted only after evaluation and, if it needs the newer runtime, behind `#if NET11_0_OR_GREATER`.
- Libraries under `src/` are marked trimmable and AOT-compatible (`IsTrimmable`, `IsAotCompatible`); the core and the
  domain use no runtime reflection.
- Tests run on Microsoft Testing Platform (enabled in `global.json`), on both runtimes.

## Alternatives considered

- **Only the newest runtime**: simpler, but excludes applications that stay on the LTS.
- **The default language version per target**: `net11.0` would compile with a newer C# than `net10.0`, and code valid
  for one target could fail on the other.

## Consequences

- Building and testing needs both runtimes installed.
- When the next LTS ships, the pair is revisited (most likely the then-current LTS plus the newest release).
- Package versions are managed centrally in `Directory.Packages.props`.

See [CONTRIBUTING](https://github.com/mrcdom/wdc-cube-csharp/blob/main/CONTRIBUTING.md).
