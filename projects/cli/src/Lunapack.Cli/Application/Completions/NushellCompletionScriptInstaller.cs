using System.IO.Abstractions;

namespace Lunapack.Cli.Application.Completions;

internal sealed class NushellCompletionScriptInstaller(
    IFileSystem fileSystem,
    string userProfileDirectory,
    string applicationDataDirectory,
    string? xdgDataHomeDirectory,
    bool isWindows,
    bool isMacOS
) : CompletionScriptInstaller(fileSystem)
{
    public override string Shell => "nushell";

    protected override string DestinationPath =>
        FileSystem.Path.Combine(
            ResolveDataDirectory(),
            "nushell",
            "vendor",
            "autoload",
            "luna-completions.nu"
        );

    private string ResolveDataDirectory()
    {
        if (isWindows)
        {
            return applicationDataDirectory;
        }

        if (!string.IsNullOrWhiteSpace(xdgDataHomeDirectory))
        {
            return xdgDataHomeDirectory;
        }

        return isMacOS
            ? FileSystem.Path.Combine(userProfileDirectory, "Library", "Application Support")
            : FileSystem.Path.Combine(userProfileDirectory, ".local", "share");
    }
}
