using System.CommandLine;
using Lunapack.Cli.Application;
using Lunapack.Cli.Application.Guidance;
using Lunapack.Cli.Packs;
using Lunapack.Cli.Packs.Manifest;
using Lunapack.Cli.Project;

namespace Lunapack.Cli.Catalog.Commands;

internal sealed class InspectPackCommandHandler(
    CatalogService catalogService,
    CliCompletionProvider completionProvider,
    WorkspaceDirectoryResolver workspaceDirectoryResolver,
    NextStepAdvisor nextStepAdvisor,
    NextStepRenderer nextStepRenderer,
    WorkflowPrerequisiteGuard prerequisiteGuard,
    CliConsole console
)
{
    public Command CreateCommand(string projectDirectory, Option<string?> workspaceOption)
    {
        var packReferenceArgument = new Argument<string>("pack-reference")
        {
            Description = "Pack ID, optionally followed by @version.",
        };
        packReferenceArgument.CompletionSources.Add(completionProvider.GetAvailablePackIds);
        var command = new Command("inspect", "Show a pack manifest.") { packReferenceArgument };
        command.SetAction(async parseResult =>
        {
            var packReferenceValue = parseResult.GetValue(packReferenceArgument);
            if (packReferenceValue is null)
            {
                return console.Fail("A pack ID is required.");
            }

            var packReference = PackReference.Parse(packReferenceValue);
            if (packReference.Value is not { } reference)
            {
                return console.Fail(packReference.Error);
            }

            return await InspectAsync(
                workspaceDirectoryResolver.Resolve(
                    projectDirectory,
                    parseResult.GetValue(workspaceOption)
                ),
                reference
            );
        });

        return command;
    }

    private async Task<int> InspectAsync(string projectDirectory, PackReference packReference)
    {
        var prerequisiteFailure = await prerequisiteGuard.RequireSourcesAsync(projectDirectory);
        if (prerequisiteFailure is not null)
        {
            return prerequisiteFailure.Value;
        }

        var catalog = await console.RunWithStatusAsync(
            $"Inspecting {packReference.Id}...",
            () => catalogService.LoadAsync(projectDirectory)
        );
        if (catalog.Value is not { } catalogPacks)
        {
            return console.Fail(catalog.Error);
        }

        var resolvedPack = PackCatalog.ResolveFromCatalog(
            catalogPacks,
            packReference.Id,
            packReference.Version
        );
        if (resolvedPack.Value is not { } pack)
        {
            var exitCode = console.Fail(resolvedPack.Error);
            nextStepRenderer.Render(
                nextStepAdvisor.Recommend(NextStepContext.PackNotFound, packReference.Id),
                "Try:"
            );
            return exitCode;
        }

        var configuration = await catalogService.LoadConfigurationAsync(projectDirectory);
        if (configuration.Value is not { } projectConfiguration)
        {
            return console.Fail(configuration.Error);
        }

        var packRemap = projectConfiguration
            .Packs.FirstOrDefault(request =>
                string.Equals(request.Id, pack.Manifest.Id, StringComparison.Ordinal)
            )
            ?.Remap;
        var renderables = PackManifestInspectionFormatter.Format(
            pack.Manifest,
            packRemap,
            projectConfiguration.Remap
        );
        foreach (var renderable in renderables)
        {
            console.Render(renderable);
        }

        nextStepRenderer.Render(
            nextStepAdvisor.Recommend(NextStepContext.PackInspected, pack.Manifest.Id),
            "Suggested commands:"
        );
        return 0;
    }
}
