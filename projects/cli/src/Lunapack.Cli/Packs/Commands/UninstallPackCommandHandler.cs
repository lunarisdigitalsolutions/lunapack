using System.CommandLine;

namespace Lunapack.Cli;

internal sealed class UninstallPackCommandHandler(
    PackLifecycleService packLifecycleService,
    WorkspaceDirectoryResolver workspaceDirectoryResolver,
    INextStepAdvisor nextStepAdvisor,
    NextStepRenderer nextStepRenderer,
    WorkflowPrerequisiteGuard prerequisiteGuard,
    CliConsole console
)
{
    public Command CreateCommand(string projectDirectory, Option<string?> workspaceOption)
    {
        var packReferenceArgument = new Argument<string[]>("pack-reference")
        {
            Arity = ArgumentArity.OneOrMore,
            Description = "Pack IDs, optionally followed by @version.",
        };
        var command = new Command("uninstall", "Remove an installed pack.")
        {
            packReferenceArgument,
        };
        command.SetAction(async parseResult =>
        {
            var packReferenceValues = parseResult.GetValue(packReferenceArgument) ?? [];
            if (packReferenceValues.Length == 0)
            {
                return console.Fail("A pack ID is required.");
            }

            var workspaceDirectory = workspaceDirectoryResolver.Resolve(
                projectDirectory,
                parseResult.GetValue(workspaceOption)
            );
            var prerequisiteFailure = await prerequisiteGuard.RequireWorkspaceAsync(
                workspaceDirectory
            );
            if (prerequisiteFailure is not null)
            {
                return prerequisiteFailure.Value;
            }

            foreach (var packReferenceValue in packReferenceValues)
            {
                var packReference = PackReference.Parse(packReferenceValue);
                if (packReference.Value is not { } reference)
                {
                    return console.Fail(packReference.Error);
                }

                var exitCode = await console.RunWithStatusAsync(
                    $"Uninstalling {reference.Id}...",
                    () => packLifecycleService.UninstallAsync(workspaceDirectory, reference)
                );
                if (exitCode != 0)
                {
                    return exitCode;
                }

                console.Info($"✓ Uninstalled {reference.Id}");
            }

            await RenderGuidanceAsync(workspaceDirectory);

            return 0;
        });

        return command;
    }

    private async Task RenderGuidanceAsync(string workspaceDirectory)
    {
        var workspace = await nextStepAdvisor.InspectWorkspaceAsync(workspaceDirectory);
        if (workspace.Value is not { } guidance)
        {
            return;
        }

        if (guidance.InstalledPackCount == 0)
        {
            console.Info(string.Empty);
            console.Info("No packs are currently installed.");
        }

        nextStepRenderer.Render(
            nextStepAdvisor.Recommend(
                guidance.InstalledPackCount == 0
                    ? NextStepContext.NoPacksRemain
                    : NextStepContext.PacksRemain
            ),
            "Suggested commands:"
        );
    }
}
