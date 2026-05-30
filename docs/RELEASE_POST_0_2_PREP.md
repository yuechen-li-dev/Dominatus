# Dominatus post-0.2 NuGet release prep

## Release Prep M3 updates

M3 is a packaging/release-cleanup milestone after the Standard WebSafety/WebContentSafety and SemanticKernel Graph sample wave. It does **not** add runtime features, publish packages, or create git tags.

### Final package versions

| Package | M3 version | Rationale |
| --- | --- | --- |
| Dominatus.Core | 0.2.1 | No package metadata or public-surface release change identified after M2.1. |
| Dominatus.OptFlow | 0.2.1 | No package metadata or public-surface release change identified after M2.1. |
| Ariadne.OptFlow | 0.2.1 | No package metadata or public-surface release change identified after M2.1. |
| Dominatus.UtilityLite | 0.2.1 | No package metadata or public-surface release change identified after M2.1. |
| Dominatus.Llm.Context | 0.1.0-preview | Preview remains unchanged for this package cleanup. |
| Dominatus.Llm.OptFlow | 0.2.1-preview | Preview remains unchanged for this package cleanup. |
| Dominatus.Actuators.Standard | 0.2.2 | Bumped from 0.2.1 because HTTP WebSafety M5-M5.4 and WebContentSafety M6-M6.1 materially changed the package after M2.1 prep. |
| Dominatus.Actuators.HomeAssistant | 0.2.1 | No package metadata or public-surface release change identified after M2.1. |
| Dominatus.Actuators.SemanticKernel | 0.1.1-preview | Bumped from 0.1.0-preview because capability profiles, ActuationPolicy integration, and Microsoft Graph Outlook mail/calendar profile helpers materially changed the package after M2.1 prep. |
| Dominatus.Server | 0.2.1-preview | Preview remains unchanged for this package cleanup. |
| Dominatus.StrideConn | 0.2.0-preview | Deferred; remains net10.0 Stride-specific preview and is not included in the M3 pack wave. |

### What changed since M2.1

- `Dominatus.Actuators.Standard` added/expanded HTTP WebSafety policy coverage: known ad/tracker hard blocks, whitelist handling, weighted signal scoring, path-scoped allowlist handling, raw-IP detection, expanded hard-block signal categories, and WebContentSafety safe-text/omission reporting with prompt-injection hardening.
- `Dominatus.Actuators.SemanticKernel` added capability profile helpers, ActuationPolicy integration, and a Microsoft Graph Outlook mail/calendar capability profile/allowlist surface.
- `samples/Dominatus.SemanticKernelGraphAssistant` demonstrates the Graph profile path without being published as a package.

### Packable package audit

Discovered packable packages under `src/`:

1. Dominatus.Core — net8.0;net10.0
2. Dominatus.OptFlow — net8.0;net10.0
3. Ariadne.OptFlow — net8.0;net10.0
4. Dominatus.UtilityLite — net8.0;net10.0
5. Dominatus.Llm.Context — net8.0;net10.0
6. Dominatus.Llm.OptFlow — net8.0;net10.0
7. Dominatus.Actuators.Standard — net8.0;net10.0
8. Dominatus.Actuators.HomeAssistant — net8.0;net10.0
9. Dominatus.Actuators.SemanticKernel — net8.0;net10.0
10. Dominatus.Server — net8.0;net10.0
11. Dominatus.StrideConn — net10.0 only, intentional Stride-specific exception/deferred package

### Metadata audit

All M3 release-wave packable projects have the required NuGet metadata: `PackageId`, `Version`, `Authors`, `Description`, `PackageTags`, `RepositoryType`, `RepositoryUrl`, `PackageProjectUrl`, `PackageLicenseExpression`, and `PackageReadmeFile`.

M3 metadata changes were limited to touched packages:

- `Dominatus.Actuators.Standard`: version bumped to 0.2.2; release notes and tags/description refreshed to mention HTTP WebSafety/WebContentSafety scope.
- `Dominatus.Actuators.SemanticKernel`: version bumped to 0.1.1-preview; release notes refreshed to mention capability profiles, ActuationPolicy integration, and Graph profile helpers.

### Dependency audit

- `Dominatus.Actuators.Standard`: references `Dominatus.Core` only; no external adblock/blocklist, DNS, browser, proxy, SemanticKernel, MCP, or LLM package dependencies.
- `Dominatus.Actuators.SemanticKernel`: references `Dominatus.Core` and `Microsoft.SemanticKernel` only; no Microsoft.Graph, Azure.Identity, MSAL, MCP, OpenAPI/Graph SDK, or A2A dependency.
- `samples/Dominatus.SemanticKernelGraphAssistant`: uses project references to Dominatus packages, including `Dominatus.Llm.OptFlow` and `Dominatus.Actuators.SemanticKernel`; it has no live Graph/Auth/LLM provider package references and is not packable/published.
- `tests/Dominatus.Release.PackageSmoke`: consumes the release wave through `PackageReference` only; no `ProjectReference`.

## Release wave packages

Included in the M3 pack wave:

- Dominatus.Core — 0.2.1
- Dominatus.OptFlow — 0.2.1
- Ariadne.OptFlow — 0.2.1
- Dominatus.UtilityLite — 0.2.1
- Dominatus.Llm.Context — 0.1.0-preview
- Dominatus.Llm.OptFlow — 0.2.1-preview
- Dominatus.Actuators.Standard — 0.2.2
- Dominatus.Actuators.HomeAssistant — 0.2.1
- Dominatus.Actuators.SemanticKernel — 0.1.1-preview
- Dominatus.Server — 0.2.1-preview

Deferred/not included:

- Dominatus.StrideConn — 0.2.0-preview (`net10.0` only; publish separately when the Stride-specific preview is ready)
- Samples, including `samples/Dominatus.SemanticKernelGraphAssistant`, because samples are not packages

## Build, test, pack, and smoke commands

```bash
dotnet build Dominatus.slnx
dotnet test Dominatus.slnx

dotnet test tests/Dominatus.Actuators.Standard.Tests/Dominatus.Actuators.Standard.Tests.csproj -f net8.0
dotnet test tests/Dominatus.Actuators.Standard.Tests/Dominatus.Actuators.Standard.Tests.csproj -f net10.0
dotnet test tests/Dominatus.Actuators.SemanticKernel.Tests/Dominatus.Actuators.SemanticKernel.Tests.csproj -f net8.0
dotnet test tests/Dominatus.Actuators.SemanticKernel.Tests/Dominatus.Actuators.SemanticKernel.Tests.csproj -f net10.0
dotnet test tests/Dominatus.SemanticKernelGraphAssistant.Tests/Dominatus.SemanticKernelGraphAssistant.Tests.csproj --framework net10.0

rm -rf artifacts/nuget-release && mkdir -p artifacts/nuget-release
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

dotnet restore tests/Dominatus.Release.PackageSmoke/Dominatus.Release.PackageSmoke.csproj --source artifacts/nuget-release --source https://api.nuget.org/v3/index.json
dotnet run --project tests/Dominatus.Release.PackageSmoke/Dominatus.Release.PackageSmoke.csproj --no-restore
```

## Release smoke coverage

The smoke project references the release wave packages by exact `PackageReference` versions only. It covers:

- Core/OptFlow/Ariadne/UtilityLite compile paths.
- Standard file/time/calendar package surfaces.
- Standard HTTP WebSafety policy deny and allowlist paths.
- Standard WebContentSafety safe content retention, prompt-injection/sponsored omission, and SafeText omission annotation.
- Llm.Context JSON/container/manifest round trip.
- Llm.OptFlow call/stream authoring helpers.
- Server stream registry/runtime read path.
- HomeAssistant options compile path.
- SemanticKernel Microsoft Graph Outlook profile creation, read allowlist inclusion for `graph.mail.list_messages`, and read allowlist exclusion for `graph.mail.send_message`.

## Publish order

1. Dominatus.Core — 0.2.1
2. Dominatus.OptFlow — 0.2.1
3. Ariadne.OptFlow — 0.2.1
4. Dominatus.UtilityLite — 0.2.1
5. Dominatus.Llm.Context — 0.1.0-preview
6. Dominatus.Llm.OptFlow — 0.2.1-preview
7. Dominatus.Actuators.Standard — 0.2.2
8. Dominatus.Actuators.HomeAssistant — 0.2.1
9. Dominatus.Actuators.SemanticKernel — 0.1.1-preview
10. Dominatus.Server — 0.2.1-preview

## Exact local push command templates

Do not run these during prep. Run only after reviewing the generated packages and setting `NUGET_API_KEY` locally.

```bash
dotnet nuget push artifacts/nuget-release/Dominatus.Core.0.2.1.nupkg --api-key $NUGET_API_KEY --source https://api.nuget.org/v3/index.json --skip-duplicate
dotnet nuget push artifacts/nuget-release/Dominatus.OptFlow.0.2.1.nupkg --api-key $NUGET_API_KEY --source https://api.nuget.org/v3/index.json --skip-duplicate
dotnet nuget push artifacts/nuget-release/Ariadne.OptFlow.0.2.1.nupkg --api-key $NUGET_API_KEY --source https://api.nuget.org/v3/index.json --skip-duplicate
dotnet nuget push artifacts/nuget-release/Dominatus.UtilityLite.0.2.1.nupkg --api-key $NUGET_API_KEY --source https://api.nuget.org/v3/index.json --skip-duplicate
dotnet nuget push artifacts/nuget-release/Dominatus.Llm.Context.0.1.0-preview.nupkg --api-key $NUGET_API_KEY --source https://api.nuget.org/v3/index.json --skip-duplicate
dotnet nuget push artifacts/nuget-release/Dominatus.Llm.OptFlow.0.2.1-preview.nupkg --api-key $NUGET_API_KEY --source https://api.nuget.org/v3/index.json --skip-duplicate
dotnet nuget push artifacts/nuget-release/Dominatus.Actuators.Standard.0.2.2.nupkg --api-key $NUGET_API_KEY --source https://api.nuget.org/v3/index.json --skip-duplicate
dotnet nuget push artifacts/nuget-release/Dominatus.Actuators.HomeAssistant.0.2.1.nupkg --api-key $NUGET_API_KEY --source https://api.nuget.org/v3/index.json --skip-duplicate
dotnet nuget push artifacts/nuget-release/Dominatus.Actuators.SemanticKernel.0.1.1-preview.nupkg --api-key $NUGET_API_KEY --source https://api.nuget.org/v3/index.json --skip-duplicate
dotnet nuget push artifacts/nuget-release/Dominatus.Server.0.2.1-preview.nupkg --api-key $NUGET_API_KEY --source https://api.nuget.org/v3/index.json --skip-duplicate
```

## Known warnings and caveats

- `Dominatus.Server` remains preview because it depends on preview `Dominatus.Llm.OptFlow` stream surfaces.
- `Dominatus.StrideConn` remains deferred and `net10.0` only.
- Samples are not published, including `Dominatus.SemanticKernelGraphAssistant`.
- No API keys, NuGet publish, or git tags are part of M3 prep.
- `artifacts/nuget-release` is a local generated artifact directory and must not be committed.
