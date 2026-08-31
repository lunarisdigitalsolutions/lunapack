using System.IO.Abstractions;

namespace Lunapack.Cli.Application.Completions;

internal sealed class NushellCompletionScriptInstaller(
    IFileSystem fileSystem,
    string userProfileDirectory,
    string applicationDataDirectory,
    bool isWindows
) : CompletionScriptInstaller(fileSystem)
{
    public override string Shell => "nushell";

    protected override string DestinationPath =>
        isWindows
            ? FileSystem.Path.Combine(
                applicationDataDirectory,
                "nushell",
                "vendor",
                "autoload",
                "luna-completions.nu"
            )
            : FileSystem.Path.Combine(
                userProfileDirectory,
                ".local",
                "share",
                "nushell",
                "vendor",
                "autoload",
                "luna-completions.nu"
            );
}
