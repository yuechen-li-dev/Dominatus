# NuGet Trusted Publishing release workflow

Dominatus publishes NuGet packages from GitHub Actions with NuGet Trusted Publishing and GitHub OIDC. The repository must not store a long-lived `NUGET_API_KEY`, and the publish workflow does not require package-publishing secrets.

## NuGet.org Trusted Publishing policy

Create or update the NuGet.org Trusted Publishing policy for the package owner with these GitHub settings:

| NuGet policy field | Value |
| --- | --- |
| Repository owner | `yuechen-li-dev` |
| Repository | `Dominatus` |
| Workflow file | `publish-nuget.yml` |
| Environment | `release` |

The workflow file lives at `.github/workflows/publish-nuget.yml`, but the NuGet policy workflow-file value is only `publish-nuget.yml`.

The policy must be configured under a NuGet account or organization that owns every package being published.

## GitHub configuration

Configure this GitHub Actions repository variable:

| Variable | Value |
| --- | --- |
| `NUGET_USER` | NuGet profile username used by `NuGet/login@v1` |

`NUGET_USER` is a NuGet username/profile name. It is not an email address and it is not an API key.

Do not add `NUGET_API_KEY` as a repository secret. The workflow uses `permissions: id-token: write` so `NuGet/login@v1` can exchange the GitHub OIDC token for a short-lived temporary NuGet API key.

## How to publish Dominatus 0.4.0

1. Confirm the NuGet Trusted Publishing policy fields above are present on NuGet.org.
2. Confirm the GitHub `release` environment exists and any desired reviewers/approvals are configured there.
3. Confirm the repository variable `NUGET_USER` contains the NuGet profile username.
4. In GitHub, open **Actions** → **Publish NuGet** → **Run workflow**.
5. Enter version `0.4.0` or keep the default.
6. Start the workflow.

The workflow restores, builds, tests, packs the publishable projects under `src/`, uploads generated `.nupkg` files as an artifact named `nuget-packages-<version>`, authenticates with `NuGet/login@v1`, and runs `dotnet nuget push` with the temporary API key and `--skip-duplicate`.

## Publishable packages

The workflow intentionally packs package projects only. It does not pack tests, sample apps, or the `Ariadne.Console` executable project.

Current 0.4 package projects:

- `src/Dominatus.Core/Dominatus.Core.csproj`
- `src/Dominatus.OptFlow/Dominatus.OptFlow.csproj`
- `src/Ariadne.OptFlow/Ariadne.OptFlow.csproj`
- `src/Dominatus.UtilityLite/Dominatus.UtilityLite.csproj`
- `src/Dominatus.Assets.Toml/Dominatus.Assets.Toml.csproj`
- `src/Dominatus.SpriteForge/Dominatus.SpriteForge.csproj`
- `src/Dominatus.Llm.Context/Dominatus.Llm.Context.csproj`
- `src/Dominatus.Llm.OptFlow/Dominatus.Llm.OptFlow.csproj`
- `src/Dominatus.Actuators.Standard/Dominatus.Actuators.Standard.csproj`
- `src/Dominatus.Actuators.HomeAssistant/Dominatus.Actuators.HomeAssistant.csproj`
- `src/Dominatus.Actuators.Payments/Dominatus.Actuators.Payments.csproj`
- `src/Dominatus.Actuators.Audio/Dominatus.Actuators.Audio.csproj`
- `src/Dominatus.Actuators.SemanticKernel/Dominatus.Actuators.SemanticKernel.csproj`
- `src/Dominatus.Server/Dominatus.Server.csproj`
- `src/Dominatus.MonoGameConn/Dominatus.MonoGameConn.csproj`
- `src/Dominatus.StrideConn/Dominatus.StrideConn.csproj`
- `src/Dominatus.GodotConn/Dominatus.GodotConn.csproj`
- `src/Dominatus.Actuators.Payments.Stripe/Dominatus.Actuators.Payments.Stripe.csproj`
- `src/Dominatus.Actuators.Payments.PayPal/Dominatus.Actuators.Payments.PayPal.csproj`

The workflow passes `/p:Version=${{ inputs.version }}` to each `dotnet pack` command so a manual run can publish `0.4.0` without editing each project file first.

## Recovery if publish fails

If authentication or publishing fails, check these items first:

- The NuGet policy repository owner is exactly `yuechen-li-dev`.
- The NuGet policy repository is exactly `Dominatus`.
- The NuGet policy workflow file is exactly `publish-nuget.yml`, not `.github/workflows/publish-nuget.yml`.
- The workflow job environment is exactly `release` and matches the NuGet policy.
- The GitHub repository variable `NUGET_USER` is set to the NuGet profile username, not an email address.
- The NuGet account or organization attached to the policy owns all packages being pushed.
- The failed package version is not already present. Re-runs use `--skip-duplicate`, but changed package contents cannot replace an already-published NuGet version.
- The package artifacts uploaded by the workflow contain the expected `.nupkg` files before the publish step.
