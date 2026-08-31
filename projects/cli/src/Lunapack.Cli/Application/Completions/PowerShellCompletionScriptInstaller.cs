using System.IO.Abstractions;

namespace Lunapack.Cli.Application.Completions;

internal sealed class PowerShellCompletionScriptInstaller(
    IFileSystem fileSystem,
    string userProfileDirectory,
    string documentsDirectory,
    bool isWindows
) : CompletionScriptInstaller(fileSystem)
{
    public override string Shell => "pwsh";

    protected override string DestinationPath =>
        isWindows
            ? FileSystem.Path.Combine(
                documentsDirectory,
                "PowerShell",
                "Microsoft.PowerShell_profile.ps1"
            )
            : FileSystem.Path.Combine(
                userProfileDirectory,
                ".config",
                "powershell",
                "Microsoft.PowerShell_profile.ps1"
            );
}
