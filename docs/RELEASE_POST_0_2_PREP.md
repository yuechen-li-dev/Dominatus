# Dominatus post-0.2 NuGet release prep (M2)

## Package wave and versions

- Dominatus.Core — 0.2.1
- Dominatus.OptFlow — 0.2.1
- Ariadne.OptFlow — 0.2.1
- Dominatus.UtilityLite — 0.2.1
- Dominatus.Actuators.Standard — 0.2.1
- Dominatus.Actuators.HomeAssistant — 0.2.1
- Dominatus.Server — 0.2.1-preview
- Dominatus.Llm.OptFlow — 0.2.1-preview
- Dominatus.Llm.Context — 0.1.0-preview
- Dominatus.Actuators.SemanticKernel — 0.1.0-preview
- Dominatus.StrideConn — 0.2.0-preview (intentionally deferred from bump; unchanged Stride preview)

## Preview coherence note

- `Dominatus.Server` is preview in this wave because its stream endpoints depend on preview `Dominatus.Llm.OptFlow`.

## Target frameworks

- net8.0;net10.0 for all wave packages except `Dominatus.StrideConn` (net10.0 intentional).

## Pack commands

```bash
rm -rf artifacts/nuget-release
mkdir -p artifacts/nuget-release

dotnet pack src/Dominatus.Core/Dominatus.Core.csproj -c Release -o artifacts/nuget-release
dotnet pack src/Dominatus.OptFlow/Dominatus.OptFlow.csproj -c Release -o artifacts/nuget-release
dotnet pack src/Ariadne.OptFlow/Ariadne.OptFlow.csproj -c Release -o artifacts/nuget-release
dotnet pack src/Dominatus.UtilityLite/Dominatus.UtilityLite.csproj -c Release -o artifacts/nuget-release
dotnet pack src/Dominatus.Llm.Context/Dominatus.Llm.Context.csproj -c Release -o artifacts/nuget-release
dotnet pack src/Dominatus.Llm.OptFlow/Dominatus.Llm.OptFlow.csproj -c Release -o artifacts/nuget-release
dotnet pack src/Dominatus.Actuators.Standard/Dominatus.Actuators.Standard.csproj -c Release -o artifacts/nuget-release
dotnet pack src/Dominatus.Actuators.HomeAssistant/Dominatus.Actuators.HomeAssistant.csproj -c Release -o artifacts/nuget-release
dotnet pack src/Dominatus.Actuators.SemanticKernel/Dominatus.Actuators.SemanticKernel.csproj -c Release -o artifacts/nuget-release
dotnet pack src/Dominatus.Server/Dominatus.Server.csproj -c Release -o artifacts/nuget-release
# optional/deferred
# dotnet pack src/Dominatus.StrideConn/Dominatus.StrideConn.csproj -c Release -o artifacts/nuget-release
```

## Smoke restore/run

```bash
dotnet restore tests/Dominatus.Release.PackageSmoke/Dominatus.Release.PackageSmoke.csproj --source artifacts/nuget-release --source https://api.nuget.org/v3/index.json
dotnet run --project tests/Dominatus.Release.PackageSmoke/Dominatus.Release.PackageSmoke.csproj --no-restore
```

## Publish order

1. Dominatus.Core
2. Dominatus.OptFlow
3. Ariadne.OptFlow
4. Dominatus.UtilityLite
5. Dominatus.Llm.Context
6. Dominatus.Llm.OptFlow
7. Dominatus.Actuators.Standard
8. Dominatus.Actuators.HomeAssistant
9. Dominatus.Actuators.SemanticKernel
10. Dominatus.Server
11. Dominatus.StrideConn (if included)

## NuGet push templates (do not run in CI without key)

```bash
dotnet nuget push artifacts/nuget-release/<PACKAGE>.<VERSION>.nupkg --api-key $NUGET_API_KEY --source https://api.nuget.org/v3/index.json --skip-duplicate
```

## Known caveats

- `Dominatus.StrideConn` remains net10.0 preview and can be published separately if Stride wave is deferred.
- No publish or tag creation is performed in this prep doc.

## Tag guidance

- Create an annotated release-prep checkpoint tag only after successful public publish verification.
- Suggested format: `v0.2.1-wave1` (or preview-specific suffixes).
