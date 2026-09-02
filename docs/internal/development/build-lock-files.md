# Build and Lock Files

The CLI lock file covers the supported release runtimes: `win-x64` and
`linux-x64`.

Local CLI builds first restore the project in locked mode without selecting a
runtime, then build the selected runtime with `--no-restore`. The default
`Build CLI` VS Code task, `./build.ps1`, and `./build.sh` use this sequence.
Both scripts always publish into the repository `publish` directory.

On Bash or Zsh, run `./build.sh --add-to-path` to prepend the publish directory
to `PATH`. The script writes a guarded entry to `.bashrc`, `.zshrc`, or
`.profile` only when the directory is not already present. Start a new shell or
source the reported profile because a child script cannot change its parent
shell environment.

On PowerShell, run `./build.ps1 -AddToPath`. The script updates the current
process PATH and, on Windows, the user PATH. Existing exact directory entries
are left unchanged.

GitHub Actions uses the same sequence. It validates the complete lock file,
then builds the requested runtime identifier without restoring. A pipeline may
therefore build one runtime, such as `win-x64`, while verifying lock data for
every supported target.

When intentionally changing package dependencies, refresh the lock file with:

```powershell
dotnet restore projects/cli/src/Lunapack.Cli/Lunapack.Cli.csproj --force-evaluate
```

Commit the updated `packages.lock.json`, then run the default build task or
one of the build scripts to verify it.
