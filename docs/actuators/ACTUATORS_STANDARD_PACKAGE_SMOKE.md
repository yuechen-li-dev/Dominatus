# Dominatus.Actuators.Standard local package smoke (M1.1)

## Purpose

M1.1 verifies `Dominatus.Actuators.Standard` as a **NuGet package consumption path**, not only as a direct `ProjectReference` development path.

This catches packaging mistakes early (dependency metadata, package contents, restore behavior) before publish.

## What this smoke verifies

The smoke flow verifies all of the following:

- `Dominatus.Core` and `Dominatus.Actuators.Standard` can be packed to local `.nupkg` files.
- A tiny consumer project restores from a local package source.
- The consumer references `Dominatus.Actuators.Standard` via `PackageReference`.
- The consumer does **not** use `ProjectReference` to `Dominatus.Actuators.Standard`.
- Minimal actuator usage works through package APIs:
  - sandboxed file write/read/exists
  - injectable time command
  - allowlisted HTTP command with fake `HttpMessageHandler` (no live network calls)

## Smoke project

Path:

- `tests/Dominatus.Actuators.Standard.PackageSmoke`

Project:

- `Dominatus.Actuators.Standard.PackageSmoke.csproj`
- Target framework: `net10.0`
- Package dependency:

```xml
<PackageReference Include="Dominatus.Actuators.Standard" Version="0.1.0" />
```

This smoke project is intentionally **not** added to `Dominatus.slnx`, because normal solution restore/test should not require pre-generated local packages.

## Local package source and artifacts

Generated local packages are written under:

- `artifacts/nuget-local`

Do not commit generated `.nupkg` files.

## Commands

From repo root:

```bash
rm -rf artifacts/nuget-local
mkdir -p artifacts/nuget-local
dotnet pack src/Dominatus.Core/Dominatus.Core.csproj -c Release -o artifacts/nuget-local
dotnet pack src/Dominatus.Actuators.Standard/Dominatus.Actuators.Standard.csproj -c Release -o artifacts/nuget-local
dotnet restore tests/Dominatus.Actuators.Standard.PackageSmoke/Dominatus.Actuators.Standard.PackageSmoke.csproj --source artifacts/nuget-local --source https://api.nuget.org/v3/index.json
dotnet run --project tests/Dominatus.Actuators.Standard.PackageSmoke/Dominatus.Actuators.Standard.PackageSmoke.csproj --no-restore
```

Expected output contains:

- `File write/read: OK`
- `File exists: OK`
- `Time: OK`
- `HTTP fake transport: OK`
- `Package smoke: OK`

## Project dependency guard

`tests/Dominatus.Actuators.Standard.Tests/PackageSmokeProjectGuardTests.cs` verifies the smoke project keeps `PackageReference` to `Dominatus.Actuators.Standard` and does not introduce a `ProjectReference` path.
