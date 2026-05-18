# Dominatus 0.2 NuGet release prep

## Package list and target versions

- Dominatus.Core — `0.2.0`
- Dominatus.OptFlow — `0.2.0`
- Ariadne.OptFlow — `0.2.0`
- Dominatus.Actuators.Standard — `0.2.0`
- Dominatus.Actuators.HomeAssistant — `0.2.0`
- Dominatus.Server — `0.2.0`
- Dominatus.Llm.OptFlow — `0.2.0-preview`
- Dominatus.StrideConn — `0.2.0-preview`

## Pack commands

```bash
mkdir -p artifacts/nuget-release

dotnet pack src/Dominatus.Core/Dominatus.Core.csproj -c Release -o artifacts/nuget-release
dotnet pack src/Dominatus.OptFlow/Dominatus.OptFlow.csproj -c Release -o artifacts/nuget-release
dotnet pack src/Ariadne.OptFlow/Ariadne.OptFlow.csproj -c Release -o artifacts/nuget-release
dotnet pack src/Dominatus.Actuators.Standard/Dominatus.Actuators.Standard.csproj -c Release -o artifacts/nuget-release
dotnet pack src/Dominatus.Actuators.HomeAssistant/Dominatus.Actuators.HomeAssistant.csproj -c Release -o artifacts/nuget-release
dotnet pack src/Dominatus.Server/Dominatus.Server.csproj -c Release -o artifacts/nuget-release
dotnet pack src/Dominatus.Llm.OptFlow/Dominatus.Llm.OptFlow.csproj -c Release -o artifacts/nuget-release
dotnet pack src/Dominatus.StrideConn/Dominatus.StrideConn.csproj -c Release -o artifacts/nuget-release
```

## Local smoke consumer restore and run

```bash
dotnet restore tests/Dominatus.Release.PackageSmoke/Dominatus.Release.PackageSmoke.csproj \
  --source artifacts/nuget-release \
  --source https://api.nuget.org/v3/index.json

dotnet run --project tests/Dominatus.Release.PackageSmoke/Dominatus.Release.PackageSmoke.csproj --no-restore
```

## NuGet push command template

```bash
dotnet nuget push artifacts/nuget-release/Dominatus.Core.0.2.0.nupkg \
  --api-key "$NUGET_API_KEY" \
  --source https://api.nuget.org/v3/index.json
```

Repeat for each package file in publish order.

## Recommended publish order

1. Dominatus.Core
2. Dominatus.OptFlow
3. Ariadne.OptFlow
4. Dominatus.Actuators.Standard
5. Dominatus.Actuators.HomeAssistant
6. Dominatus.Server
7. Dominatus.Llm.OptFlow
8. Dominatus.StrideConn

## Known caveats

- `Dominatus.Llm.OptFlow` and `Dominatus.StrideConn` are versioned as preview (`0.2.0-preview`).
- Stride package restore can be heavier than the other packages.
- MonoGame warning in FishTank sample is tolerated if present during full solution operations.

## Suggested git tag commands

```bash
git tag v0.2.0
git push origin v0.2.0
```
