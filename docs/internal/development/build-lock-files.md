# Build and Lock Files

The CLI lock file covers the supported release runtimes: `win-x64` and
`linux-x64`.

Local CLI builds first restore the project in locked mode without selecting a
runtime, then build the selected runtime with `--no-restore`. The default
`Build CLI` VS Code task and `./build.ps1` use this sequence.

GitHub Actions uses the same sequence. It validates the complete lock file,
then builds the requested runtime identifier without restoring. A pipeline may
therefore build one runtime, such as `win-x64`, while verifying lock data for
every supported target.

When intentionally changing package dependencies, refresh the lock file with:

```powershell
dotnet restore projects/cli/src/Lunapack.Cli/Lunapack.Cli.csproj --force-evaluate
```

Commit the updated `packages.lock.json`, then run the default build task or
`./build.ps1` to verify it.
