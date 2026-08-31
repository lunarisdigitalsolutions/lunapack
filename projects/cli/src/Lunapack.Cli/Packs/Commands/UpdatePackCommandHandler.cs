using System.CommandLine;
using Lunapack.Cli.Application;
using Lunapack.Cli.Application.CommandExecution;
using Lunapack.Cli.Application.Guidance;
using Lunapack.Cli.Links;
using Lunapack.Cli.Packs.Planning;
using Lunapack.Cli.Project;
using Lunapack.Cli.Trust;

namespace Lunapack.Cli.Packs.Commands;

internal sealed class UpdatePackCommandHandler(
    PackUpdateService packUpdateService,
    LinkCommandDispatcher linkCommandDispatcher,
    PackUpdateSelectionService updateSelectionService,
    IPackUpdatePrompter packUpdatePrompter,
    WorkspaceDirectoryResolver workspaceDirectoryResolver,
    NextStepAdvisor nextStepAdvisor,
    NextStepRenderer nextStepRenderer,
    WorkflowPrerequisiteGuard prerequisiteGuard,
    CliConsole console
)
{
    public Command CreateCommand(string projectDirectory, Option<string?> workspaceOption)
    {
        var packReferenceArgument = CreatePackReferenceArgument();
        var promptOption = CreatePromptOption();
        var dryRunOption = CreateDryRunOption();
        var noFileChangeOutputOption = CreateNoFileChangeOutputOption();
        var acceptSourcesOption = CreateAcceptSourcesOption();
        var scriptsOption = CreateScriptsOption();
        var skipInstructionsOption = CreateSkipInstructionsOption();
        var command = new Command("update", "Update installed packs.")
        {
            packReferenceArgument,
            promptOption,
            dryRunOption,
            noFileChangeOutputOption,
            acceptSourcesOption,
            scriptsOption,
            skipInstructionsOption,
        };
        command.SetAction(parseResult =>
            ExecuteCommandAsync(
                projectDirectory,
                workspaceOption,
                parseResult,
                packReferenceArgument,
                promptOption,
                dryRunOption,
                noFileChangeOutputOption,
                acceptSourcesOption,
                scriptsOption,
                skipInstructionsOption
            )
        );

        return command;
    }

    private async Task<int> ExecuteCommandAsync(
        string projectDirectory,
        Option<string?> workspaceOption,
        ParseResult parseResult,
        Argument<string[]> packReferenceArgument,
        Option<bool> promptOption,
        Option<bool> dryRunOption,
        Option<bool> noFileChangeOutputOption,
        Option<bool> acceptSourcesOption,
        Option<string?> scriptsOption,
        Option<bool> skipInstructionsOption
    )
    {
        var referenceValues = parseResult.GetValue(packReferenceArgument) ?? [];
        var parsedReferences = ParseReferences(referenceValues);
        if (parsedReferences.Value is not { } references)
        {
            return console.Fail(parsedReferences.Error);
        }

        var workspaceDirectory = workspaceDirectoryResolver.Resolve(
            projectDirectory,
            parseResult.GetValue(workspaceOption)
        );
        var prerequisiteFailure = await prerequisiteGuard.RequireSourcesAsync(workspaceDirectory);
        if (prerequisiteFailure is not null)
        {
            return prerequisiteFailure.Value;
        }

        var dryRun = parseResult.GetValue(dryRunOption);
        var showFileChanges = !parseResult.GetValue(noFileChangeOutputOption);
        var acceptSources = parseResult.GetValue(acceptSourcesOption);
        var scriptMode = ScriptExecutionMode.Parse(
            parseResult.GetValue(scriptsOption) ?? ScriptExecutionMode.Prompt.Value
        );
        if (scriptMode.Value is not { } parsedScriptMode)
        {
            return console.Fail(scriptMode.Error);
        }

        var prompt = parseResult.GetValue(promptOption);
        if (prompt && references.Count > 0)
        {
            return console.Fail("The --prompt option is only available when updating all packs.");
        }

        var skipInstructions = parseResult.GetValue(skipInstructionsOption);
        if (prompt)
        {
            return await HandleResultAsync(
                await PromptAndUpdateAsync(
                    workspaceDirectory,
                    dryRun,
                    parsedScriptMode,
                    skipInstructions,
                    acceptSources
                ),
                dryRun,
                showFileChanges
            );
        }

        if (references.Count == 0)
        {
            return await HandleResultAsync(
                await UpdateAsync(
                    workspaceDirectory,
                    null,
                    dryRun,
                    parsedScriptMode,
                    skipInstructions,
                    "Updating packs...",
                    acceptSources
                ),
                dryRun,
                showFileChanges
            );
        }

        return await UpdateRequestedPacksAsync(
            workspaceDirectory,
            referenceValues,
            references,
            dryRun,
            showFileChanges,
            parsedScriptMode,
            skipInstructions,
            acceptSources
        );
    }

    private async Task<int> UpdateRequestedPacksAsync(
        string workspaceDirectory,
        string[] referenceValues,
        IReadOnlyList<PackReference> references,
        bool dryRun,
        bool showFileChanges,
        ScriptExecutionMode scriptMode,
        bool skipInstructions,
        bool acceptSources
    )
    {
        for (var index = 0; index < referenceValues.Length; index++)
        {
            var referenceValue = referenceValues[index];
            var linkExitCode = await linkCommandDispatcher.TryUpdateAsync(
                workspaceDirectory,
                referenceValue
            );
            if (linkExitCode is not null)
            {
                if (linkExitCode.Value != 0)
                {
                    return linkExitCode.Value;
                }

                continue;
            }

            var reference = references[index];
            var exitCode = await HandleResultAsync(
                await UpdateAsync(
                    workspaceDirectory,
                    reference,
                    dryRun,
                    scriptMode,
                    skipInstructions,
                    $"Updating {reference.Id}...",
                    acceptSources
                ),
                dryRun,
                showFileChanges
            );
            if (exitCode != 0)
            {
                return exitCode;
            }
        }

        return 0;
    }

    private static Argument<string[]> CreatePackReferenceArgument() =>
        new("pack-reference")
        {
            Arity = ArgumentArity.ZeroOrMore,
            Description = "Pack IDs, optionally followed by @version.",
        };

    private static Option<bool> CreatePromptOption() =>
        new("--prompt", "-p") { Description = "Confirm each available update before applying it." };

    private static Option<bool> CreateDryRunOption() =>
        new("--dry-run", "-D") { Description = "Plan updates without modifying files or state." };

    private static Option<bool> CreateNoFileChangeOutputOption() =>
        new("--no-file-change-output")
        {
            Description = "Do not list managed-file changes after updates.",
        };

    private static Option<bool> CreateAcceptSourcesOption() =>
        new("--accept-sources")
        {
            Description = "Approve conflict-free external source additions.",
        };

    private static Option<string?> CreateScriptsOption() =>
        new("--scripts") { Description = "Lifecycle script mode: prompt, run, or skip." };

    private static Option<bool> CreateSkipInstructionsOption() =>
        new("--skip-instructions") { Description = "Skip lifecycle instructions." };

    private Task<PackUpdateService.UpdateResult> UpdateAsync(
        string projectDirectory,
        PackReference? reference,
        bool dryRun,
        ScriptExecutionMode scriptMode,
        bool skipInstructions,
        string status,
        bool acceptSources
    ) =>
        scriptMode == ScriptExecutionMode.Prompt
            ? packUpdateService.UpdateAsync(
                projectDirectory,
                reference,
                dryRun,
                scriptMode,
                skipInstructions,
                acceptSources
            )
            : console.RunWithStatusAsync(
                status,
                () =>
                    packUpdateService.UpdateAsync(
                        projectDirectory,
                        reference,
                        dryRun,
                        scriptMode,
                        skipInstructions,
                        acceptSources
                    )
            );

    private async Task<PackUpdateService.UpdateResult> PromptAndUpdateAsync(
        string projectDirectory,
        bool dryRun,
        ScriptExecutionMode scriptMode,
        bool skipInstructions,
        bool acceptSources
    )
    {
        var availableUpdates = await updateSelectionService.GetAvailableAsync(projectDirectory);
        if (availableUpdates.Value is not { } updates)
        {
            return PackUpdateService.UpdateResult.Failure(
                availableUpdates.Error ?? "Unable to select available updates."
            );
        }

        var confirmedIds = updates
            .Where(packUpdatePrompter.Confirm)
            .Select(update => update.RequestedRoot.Id)
            .ToHashSet(StringComparer.Ordinal);
        return await packUpdateService.UpdateSelectedAsync(
            projectDirectory,
            confirmedIds,
            dryRun,
            scriptMode,
            skipInstructions,
            acceptSources
        );
    }

    private static ManifestOperationResult<IReadOnlyList<PackReference>> ParseReferences(
        string[] values
    )
    {
        var references = new List<PackReference>(values.Length);
        foreach (var value in values)
        {
            var parsedReference = PackReference.Parse(value);
            if (parsedReference.Value is not { } reference)
            {
                return ManifestOperationResult<IReadOnlyList<PackReference>>.Failure(
                    parsedReference.Error ?? "Invalid pack reference."
                );
            }

            references.Add(reference);
        }

        return ManifestOperationResult<IReadOnlyList<PackReference>>.Success(references);
    }

    private async Task<int> HandleResultAsync(
        PackUpdateService.UpdateResult result,
        bool dryRun,
        bool showFileChanges
    )
    {
        if (result.Error is not null)
        {
            return console.Fail(result.Error);
        }

        if (result.IsLifecycleFailure)
        {
            return 1;
        }

        if (dryRun)
        {
            foreach (
                var line in PackDryRunFormatter.FormatUpdate(
                    result.Outcomes,
                    result.FileChangePlan ?? new PackUpdatePlan([]),
                    result.ProposedSourceSwitch
                )
            )
            {
                console.MarkupInfo(line);
            }
        }
        else
        {
            console.Info(string.Empty);
            WriteOutcomes(console, result.Outcomes);
            if (showFileChanges && result.FileChangePlan is not null)
            {
                foreach (
                    var line in PackDryRunFormatter.FormatAppliedFileChanges(result.FileChangePlan)
                )
                {
                    console.MarkupInfo(line);
                }
            }
            var updatedCount = result.Outcomes.Count(outcome => !outcome.IsCurrent);
            if (updatedCount > 0)
            {
                nextStepRenderer.Render(
                    nextStepAdvisor.Recommend(NextStepContext.PacksUpdated),
                    "Suggested commands:"
                );
            }
        }

        return 0;
    }

    private static void WriteOutcomes(
        CliConsole console,
        IReadOnlyList<PackUpdateService.UpdateOutcome> outcomes
    )
    {
        if (outcomes.Count == 0)
        {
            console.Info("No updates are available.");
            return;
        }

        foreach (var outcome in outcomes)
        {
            var message = outcome.IsCurrent
                ? $"{outcome.Id} {outcome.CurrentVersion} is current."
                : $"Updated '{outcome.Id}' (version '{outcome.SelectedVersion}')";
            if (outcome.IsCurrent)
            {
                console.Info(message);
            }
            else
            {
                console.Success(message);
            }
        }
    }
}
