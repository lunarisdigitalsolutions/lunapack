using System.CommandLine;
using System.IO.Abstractions;
using Lunapack.Cli.Application;
using Lunapack.Cli.Application.Guidance;
using Lunapack.Cli.Links;
using Lunapack.Cli.Project;
using Lunapack.Cli.Trust;

namespace Lunapack.Cli.Packs.Commands;

internal sealed class UninstallPackCommandHandler(
    IFileSystem fileSystem,
    PackLifecycleService packLifecycleService,
    LinkCommandDispatcher linkCommandDispatcher,
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
        var packReferenceArgument = new Argument<string[]>("pack-reference")
        {
            Arity = ArgumentArity.OneOrMore,
            Description = "Pack IDs, optionally followed by @version.",
            HelpName = "pack-reference",
        };
        packReferenceArgument.CompletionSources.Add(completionProvider.GetInstalledReferences);
        var parameterOption = new Option<string[]>("--parameter", "-p")
        {
            Description = "Lifecycle template parameter in <name>=<value> form.",
        };
        var noVariablesOption = new Option<bool>("--no-variables", "-nv")
        {
            Description = "Do not bind matching project variables for lifecycle hooks.",
        };
        var skipVariableOption = new Option<string[]>("--skip-variable", "-sv")
        {
            Description = "Project variable name to skip during lifecycle hook binding.",
        };
        skipVariableOption.CompletionSources.Add(completionProvider.GetConfiguredVariableNames);
        var scriptsOption = new Option<string?>("--scripts")
        {
            Description = "Lifecycle script mode: prompt, run, or skip.",
        };
        scriptsOption.CompletionSources.Add("prompt", "run", "skip");
        var skipInstructionsOption = new Option<bool>("--skip-instructions")
        {
            Description = "Skip lifecycle instructions.",
        };
        var command = new Command("uninstall", "Remove an installed pack.")
        {
            packReferenceArgument,
            parameterOption,
            noVariablesOption,
            skipVariableOption,
            scriptsOption,
            skipInstructionsOption,
        };
        var options = (
            PackReferences: packReferenceArgument,
            Parameters: parameterOption,
            NoVariables: noVariablesOption,
            SkippedVariables: skipVariableOption,
            Scripts: scriptsOption,
            SkipInstructions: skipInstructionsOption
        );
        command.SetAction(parseResult =>
            ExecuteAsync(projectDirectory, workspaceOption, parseResult, options)
        );

        return command;
    }

    private async Task<int> ExecuteAsync(
        string projectDirectory,
        Option<string?> workspaceOption,
        ParseResult parseResult,
        (
            Argument<string[]> PackReferences,
            Option<string[]> Parameters,
            Option<bool> NoVariables,
            Option<string[]> SkippedVariables,
            Option<string?> Scripts,
            Option<bool> SkipInstructions
        ) options
    )
    {
        var packReferenceValues = parseResult.GetValue(options.PackReferences) ?? [];
        if (packReferenceValues.Length == 0)
        {
            return console.Fail("A pack ID is required.");
        }

        var workspaceDirectory = workspaceDirectoryResolver.Resolve(
            projectDirectory,
            parseResult.GetValue(workspaceOption)
        );
        var prerequisiteFailure = await prerequisiteGuard.RequireWorkspaceAsync(workspaceDirectory);
        if (prerequisiteFailure is not null)
        {
            return prerequisiteFailure.Value;
        }

        var scriptMode = ScriptExecutionMode.Parse(
            parseResult.GetValue(options.Scripts) ?? ScriptExecutionMode.Prompt.Value
        );
        if (scriptMode.Value is not { } parsedScriptMode)
        {
            return console.Fail(scriptMode.Error);
        }

        foreach (var packReferenceValue in packReferenceValues)
        {
            var hookRequest = PackInstallationRequest.Create(
                fileSystem,
                workspaceDirectory,
                packReferenceValue,
                null,
                false,
                parseResult.GetValue(options.Parameters) ?? [],
                parseResult.GetValue(options.NoVariables),
                parseResult.GetValue(options.SkippedVariables) ?? [],
                scriptMode: parsedScriptMode,
                skipInstructions: parseResult.GetValue(options.SkipInstructions)
            );
            if (hookRequest.Value is not { } request)
            {
                return console.Fail(hookRequest.Error);
            }

            var exitCode = await UninstallAsync(workspaceDirectory, request);
            if (exitCode != 0)
            {
                return exitCode;
            }
        }

        await RenderGuidanceAsync(workspaceDirectory);
        return 0;
    }

    private async Task<int> UninstallAsync(
        string workspaceDirectory,
        PackInstallationRequest hookRequest
    )
    {
        var packReferenceValue = hookRequest.PackReference.Version is { } version
            ? $"{hookRequest.PackReference.Id}@{version}"
            : hookRequest.PackReference.Id;
        var linkExitCode = await linkCommandDispatcher.TryUninstallAsync(
            workspaceDirectory,
            packReferenceValue
        );
        if (linkExitCode is not null)
        {
            return linkExitCode.Value;
        }

        var packReference = PackReference.Parse(packReferenceValue);
        if (packReference.Value is not { } reference)
        {
            return console.Fail(packReference.Error);
        }

        TimeSpan? managedFileChangesDuration = null;
        var exitCode = await console.RunWithStatusAsync(
            $"Uninstalling {reference.Id}...",
            () =>
                packLifecycleService.UninstallAsync(
                    workspaceDirectory,
                    hookRequest,
                    duration => managedFileChangesDuration = duration
                )
        );
        if (exitCode != 0)
        {
            return exitCode;
        }

        console.Info(string.Empty);
        console.Success(
            $"Uninstalled '{reference.Id}' in {CliDuration.Format(managedFileChangesDuration ?? TimeSpan.Zero)}"
        );
        return 0;
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
