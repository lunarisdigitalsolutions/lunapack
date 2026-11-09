using System.CommandLine;
using System.IO.Abstractions;
using Lunapack.Cli.Application;
using Lunapack.Cli.Application.Paths;
using Lunapack.Cli.Packs.Publishing;

namespace Lunapack.Cli.Packs.Commands;

internal sealed class PublishPackCommandHandler(
    IFileSystem fileSystem,
    PackPublishingService packPublishingService,
    CliCompletionProvider completionProvider,
    WorkspaceDirectoryResolver workspaceDirectoryResolver,
    CliConsole console
)
{
    public Command CreateCommand(string projectDirectory, Option<string?> workspaceOption)
    {
        var folderArgument = new Argument<string>("folder")
        {
            Description = "Directory containing the pack's root pack.yml.",
        };
        var targetOption = new Option<string>("--target")
        {
            Description = "Configured source to publish to.",
            Required = true,
        };
        targetOption.CompletionSources.Add(completionProvider.GetConfiguredSourceNames);

        var command = new Command("publish", "Publish a validated pack to a configured source.")
        {
            folderArgument,
            targetOption,
        };
        command.SetAction(async parseResult =>
        {
            var folder = parseResult.GetValue(folderArgument);
            var target = parseResult.GetValue(targetOption);
            if (string.IsNullOrWhiteSpace(folder) || string.IsNullOrWhiteSpace(target))
            {
                return console.Fail("A pack folder and --target source are required.");
            }

            var workspace = workspaceDirectoryResolver.Resolve(
                projectDirectory,
                parseResult.GetValue(workspaceOption)
            );
            var relativePath = ProjectPath.NormalizeProjectRelativePath(
                fileSystem,
                workspace,
                folder
            );
            if (relativePath.Value is not { } normalizedPath)
            {
                return console.Fail(relativePath.Error);
            }

            var packDirectory = fileSystem.Path.Combine(workspace, normalizedPath);
            var result = await packPublishingService.PublishAsync(workspace, packDirectory, target);
            if (result.Value is not { } publication)
            {
                return console.Fail(result.Error);
            }

            console.Success(
                $"Published {publication.PackId}@{publication.Version} to source '{publication.SourceName}' as {publication.Reference} (manifest {publication.ManifestDigest})."
            );
            return 0;
        });

        return command;
    }
}
