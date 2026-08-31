using System.IO.Abstractions;

namespace Lunapack.Cli.Application.Completions;

internal abstract class CompletionScriptInstaller(IFileSystem fileSystem)
{
    protected IFileSystem FileSystem { get; } = fileSystem;

    public abstract string Shell { get; }

    protected abstract string DestinationPath { get; }

    public CompletionScriptInstallPlan CreatePlan(string script) => new(script, DestinationPath);

    public void Install(CompletionScriptInstallPlan plan)
    {
        var existingContent = FileSystem.File.Exists(plan.DestinationPath)
            ? FileSystem.File.ReadAllText(plan.DestinationPath)
            : null;
        if (existingContent?.Contains(plan.Script, StringComparison.Ordinal) is true)
        {
            return;
        }

        var directory = FileSystem.Path.GetDirectoryName(plan.DestinationPath);
        if (directory is not null)
        {
            FileSystem.Directory.CreateDirectory(directory);
        }

        var separator =
            existingContent is null or "" || existingContent.EndsWith('\n')
                ? string.Empty
                : Environment.NewLine;
        FileSystem.File.AppendAllText(plan.DestinationPath, separator + plan.Script);
    }
}
