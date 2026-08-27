using System.CommandLine;

namespace Lunapack.Cli;

internal sealed class UpdatePackCommandHandler(
    PackUpdateService packUpdateService,
    PackUpdateSelectionService updateSelectionService,
    IPackUpdatePrompter packUpdatePrompter,
    WorkspaceDirectoryResolver workspaceDirectoryResolver,
    INextStepAdvisor nextStepAdvisor,
    NextStepRenderer nextStepRenderer,
    WorkflowPrerequisiteGuard prerequisiteGuard,
    CliConsole console
)
{
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Maintainability",
        "MA0051:Method is too long",
        Justification = "CLI option definitions remain collocated with their command action."
    )]
    public Command CreateCommand(string projectDirectory, Option<string?> workspaceOption)
    {
        var packReferenceArgument = new Argument<string[]>("pack-reference")
        {
            Arity = ArgumentArity.ZeroOrMore,
            Description = "Pack IDs, optionally followed by @version.",
        };
        var promptOption = new Option<bool>("--prompt", "-p")
        {
            Description = "Confirm each available update before applying it.",
        };
        var dryRunOption = new Option<bool>("--dry-run", "-D")
        {
            Description = "Plan updates without modifying files or state.",
        };
        var acceptSourcesOption = new Option<bool>("--accept-sources")
        {
            Description = "Approve conflict-free external source additions.",
        };
        var scriptsOption = new Option<string?>("--scripts")
        {
            Description = "Lifecycle script mode: prompt, run, or skip.",
        };
        var command = new Command("update", "Update installed packs.")
        {
            packReferenceArgument,
            promptOption,
            dryRunOption,
            acceptSourcesOption,
            scriptsOption,
        };
        command.SetAction(async parseResult =>
        {
            var parsedReferences = ParseReferences(
                parseResult.GetValue(packReferenceArgument) ?? []
            );
            if (parsedReferences.Value is not { } references)
            {
                return console.Fail(parsedReferences.Error);
            }

            var workspaceDirectory = workspaceDirectoryResolver.Resolve(
                projectDirectory,
                parseResult.GetValue(workspaceOption)
            );
            var prerequisiteFailure = await prerequisiteGuard.RequireSourcesAsync(
                workspaceDirectory
            );
            if (prerequisiteFailure is not null)
            {
                return prerequisiteFailure.Value;
            }

            var dryRun = parseResult.GetValue(dryRunOption);
            var acceptSources = parseResult.GetValue(acceptSourcesOption);
            var scriptMode = ScriptExecutionMode.Parse(
                parseResult.GetValue(scriptsOption) ?? ScriptExecutionMode.Prompt.Value
            );
            if (scriptMode.Value is not { } parsedScriptMode)
            {
                return console.Fail(scriptMode.Error);
            }
            if (parseResult.GetValue(promptOption) && references.Count > 0)
            {
                return console.Fail(
                    "The --prompt option is only available when updating all packs."
                );
            }

            if (parseResult.GetValue(promptOption))
            {
                return await HandleResultAsync(
                    await PromptAndUpdateAsync(
                        workspaceDirectory,
                        dryRun,
                        parsedScriptMode,
                        acceptSources
                    ),
                    dryRun
                );
            }

            if (references.Count == 0)
            {
                return await HandleResultAsync(
                    await console.RunWithStatusAsync(
                        "Updating packs...",
                        () =>
                            packUpdateService.UpdateAsync(
                                workspaceDirectory,
                                null,
                                dryRun,
                                parsedScriptMode,
                                acceptSources
                            )
                    ),
                    dryRun
                );
            }

            foreach (var reference in references)
            {
                var exitCode = await HandleResultAsync(
                    await console.RunWithStatusAsync(
                        $"Updating {reference.Id}...",
                        () =>
                            packUpdateService.UpdateAsync(
                                workspaceDirectory,
                                reference,
                                dryRun,
                                parsedScriptMode,
                                acceptSources
                            )
                    ),
                    dryRun
                );
                if (exitCode != 0)
                {
                    return exitCode;
                }
            }

            return 0;
        });

        return command;
    }

    private async Task<PackUpdateService.UpdateResult> PromptAndUpdateAsync(
        string projectDirectory,
        bool dryRun,
        ScriptExecutionMode scriptMode,
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

    private async Task<int> HandleResultAsync(PackUpdateService.UpdateResult result, bool dryRun)
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
                    result.DryRunPlan ?? new PackUpdatePlan([]),
                    result.ProposedSourceSwitch
                )
            )
            {
                console.Info(line);
            }
        }
        else
        {
            WriteOutcomes(console, result.Outcomes);
            var updatedCount = result.Outcomes.Count(outcome => !outcome.IsCurrent);
            if (updatedCount > 0)
            {
                console.Info($"✓ Updated {updatedCount} packs");
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
                : $"{outcome.Id} {outcome.CurrentVersion} -> {outcome.SelectedVersion}";
            console.Info(message);
        }
    }
}
