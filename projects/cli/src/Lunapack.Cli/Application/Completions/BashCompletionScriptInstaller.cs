using System.IO.Abstractions;

namespace Lunapack.Cli.Application.Completions;

internal sealed class BashCompletionScriptInstaller(
    IFileSystem fileSystem,
    string userProfileDirectory
) : CompletionScriptInstaller(fileSystem)
{
    public override string Shell => "bash";

    protected override string DestinationPath =>
        FileSystem.Path.Combine(userProfileDirectory, ".bashrc");
}
