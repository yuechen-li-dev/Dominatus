# Dominatus 1.0 release checklist

- Run Release restore, build, and tests on net8.0 and net10.0.
- Pack only the manifest packages to a clean local feed.
- Run the package-only smoke and inspect the OptFlow analyzer path.
- Dispatch `Dominatus 1.0 release` with publishing and GitHub release creation disabled for validation.
- For an authorized release only, enable each option deliberately and supply `NUGET_API_KEY`.
- Do not tag, publish, or create a release from branch pushes or pull requests.
