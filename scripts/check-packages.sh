#!/usr/bin/env bash
# Installs the packages in a folder into a new project outside the repository and builds and runs it: what someone
# who takes them from NuGet would do. Usage:  scripts/check-packages.sh <packages folder> <version>
set -euo pipefail

packages="$(cd "$1" && pwd)"
version="$2"
work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT

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

references=""
for package in "$packages"/Wdc.*."$version".nupkg; do
    id="$(basename "$package" ".$version.nupkg")"
    references+="    <PackageReference Include=\"$id\" Version=\"$version\" />"$'\n'
done
[ -n "$references" ] || { echo "check-packages: no Wdc.* $version package in $packages" >&2; exit 1; }

cat > "$work/Check.csproj" <<XML
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
  </PropertyGroup>
  <ItemGroup>
$references  </ItemGroup>
</Project>
XML

cat > "$work/Program.cs" <<'CS'
using Wdc.Cube;
using Wdc.Cube.Testing;

var dispatcher = new ManualDispatcher();
var place = Place.Root.Child<CubeApplication>("check", _ => throw new NotSupportedException());
Console.WriteLine(place.PathName == "check" && dispatcher.PendingCount == 0 ? "packages ok" : "unexpected");
CS

dotnet run --project "$work/Check.csproj"
