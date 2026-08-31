using System.IO.Abstractions;

namespace Lunapack.Cli.Application.Completions;

internal sealed class FishCompletionScriptInstaller(
    IFileSystem fileSystem,
    string userProfileDirectory
) : CompletionScriptInstaller(fileSystem)
{
    public override string Shell => "fish";

    protected override string DestinationPath =>
        FileSystem.Path.Combine(
            userProfileDirectory,
            ".config",
            "fish",
            "conf.d",
            "luna-completions.fish"
        );
}
