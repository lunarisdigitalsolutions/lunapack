# Install Luna

Luna is the LunaPack command-line interface. Install it from a GitHub Release,
npm, or NuGet.

## GitHub Release

Download the archive for your operating system, extract it, and add the
contained executable to your process path.

- Windows x64: `luna-cli-<version>-win-x64.zip`, containing `luna.exe`.
- Linux x64: `luna-cli-<version>-linux-x64.tar.gz`, containing `luna`.
- Linux Arm64: `luna-cli-<version>-linux-arm64.tar.gz`, containing `luna`.
- macOS x64: `luna-cli-<version>-osx-x64.tar.gz`, containing `luna`.
- macOS Arm64: `luna-cli-<version>-osx-arm64.tar.gz`, containing `luna`.

Verify downloaded archives with the release `SHA256SUMS.txt` file.

## npm

```powershell
npm install --global @lunarisdigitalsolutions/lunapack
```

The entry package installs the matching native payload through npm optional
dependencies.

## .NET Tool

```powershell
dotnet tool install --global Lunaris.Lunapack.Luna
```

The .NET SDK automatically installs the self-contained package matching your
supported operating system and architecture.

## Docker

Run the Linux x64 image from GitHub Container Registry with the project mounted
at `/workspace`:

```powershell
docker run --rm --volume "${PWD}:/workspace" ghcr.io/lunarisdigitalsolutions/lunapack:latest --help
```

The image runs as a non-root user. On Linux, pass
`--user "$(id -u):$(id -g)"` when generated files must use the invoking user's
host ownership. Luna reads and writes only through the mounted project path.

## Update Luna

Update the installation method already in use:

```powershell
dotnet tool update --global Lunaris.Lunapack.Luna
npm install --global @lunarisdigitalsolutions/lunapack@latest
docker pull ghcr.io/lunarisdigitalsolutions/lunapack:latest
```

For reproducible automation, pin an exact package or image version instead of
using `latest`.

Confirm the executable is available before starting a project:

```powershell
luna --help
```

For MVP evaluation, use the build supplied by the team running the evaluation.
When seeking help, include the binary version, operating system, workspace
path, command, and complete error output.
