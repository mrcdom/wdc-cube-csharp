#!/usr/bin/env bash
# Installs the template package of a folder, creates a project from it outside the repository and builds, checks the
# formatting of and tests what it made, for each target framework: what someone who takes it from NuGet would do.
# The Wdc.* packages come from the same folder. Usage:  scripts/check-template.sh <packages folder> <version>
#
# The generated tests run on the PostgreSQL server of TEST_POSTGRES (WDC_TEST_POSTGRES when it is not set); without
# either, they are skipped.
set -euo pipefail

packages="$(cd "$1" && pwd)"
version="$2"
template="$packages/Wdc.Cube.Templates.$version.nupkg"
[ -f "$template" ] || { echo "check-template: no $template" >&2; exit 1; }

work="$(cd "$(mktemp -d)" && pwd -P)" # the real path: on macOS /var is a link, and MSBuild would see two
trap 'rm -rf "$work"' EXIT

# Its own template store and package cache: nothing installed for the user, nothing stale from an earlier run.
hive="$work/hive"
export NUGET_PACKAGES="$work/nuget"
export TEST_POSTGRES="${TEST_POSTGRES:-${WDC_TEST_POSTGRES:-}}"
[ -n "$TEST_POSTGRES" ] || echo "check-template: TEST_POSTGRES is not set: the database tests will be skipped." >&2

cat > "$work/nuget.config" <<XML
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="local" value="$packages" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
  <packageSourceMapping>
    <packageSource key="local"><package pattern="Wdc.*" /></packageSource>
    <packageSource key="nuget.org"><package pattern="*" /></packageSource>
  </packageSourceMapping>
</configuration>
XML

dotnet new install "$template" --debug:custom-hive "$hive"

# <framework> <name> [format]: the default name gets the template's own formatting; another one may sort the usings
# differently (the application's namespaces among the others), which dotnet format would only reorder.
check() {
    local app="$work/$1/$2"
    echo "check-template: $1, $2"
    dotnet new cube-blazor --debug:custom-hive "$hive" -n "$2" -o "$app" --Framework "$1"
    cp "$work/nuget.config" "$app/nuget.config"
    dotnet build "$app/$2.slnx"
    if [ "${3:-}" = format ]; then
        dotnet format "$app/$2.slnx" --verify-no-changes --no-restore
    fi
    dotnet test --solution "$app/$2.slnx" --no-build
}

check net10.0 MyCubeApp format
check net11.0 Acme.Tasks
