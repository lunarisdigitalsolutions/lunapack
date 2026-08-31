using System.IO.Abstractions;

namespace Lunapack.Cli.Application.Completions;

internal sealed class ZshCompletionScriptInstaller(
    IFileSystem fileSystem,
    string userProfileDirectory
) : CompletionScriptInstaller(fileSystem)
{
    public override string Shell => "zsh";

    protected override string DestinationPath =>
        FileSystem.Path.Combine(userProfileDirectory, ".zshrc");
}
