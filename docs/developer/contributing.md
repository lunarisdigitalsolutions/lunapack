# Contributor setup

Use the .NET SDK selected by `global.json` and a supported Node.js release.
Restore locked dependencies from the repository root:

```powershell
dotnet restore projects/cli/src/Lunapack.slnx --locked-mode
npm ci
```

Run the full local baseline before review:

```powershell
dotnet build projects/cli/src/Lunapack.slnx --configuration Release
dotnet test --solution projects/cli/src/Lunapack.slnx --configuration Release --no-build
npm run lint
npm run --prefix projects/website build
```

Keep public usage guidance in `docs/developer`, maintainer implementation detail
in `docs/internal`, and product direction in `docs/product`. Update tests,
schemas, help, examples, packs, documentation, and changelog together when a
public interface changes. Root `CONTRIBUTING.md` contains pull-request and
community requirements.
