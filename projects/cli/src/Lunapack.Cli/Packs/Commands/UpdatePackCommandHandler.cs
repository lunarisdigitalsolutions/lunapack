using System.CommandLine;
using System.IO.Abstractions;
using Lunapack.Cli.Application;
using Lunapack.Cli.Application.CommandExecution;
using Lunapack.Cli.Application.Guidance;
using Lunapack.Cli.Links;
using Lunapack.Cli.Packs.ManagedFiles;
using Lunapack.Cli.Packs.Planning;
using Lunapack.Cli.Project;
using Lunapack.Cli.Trust;

namespace Lunapack.Cli.Packs.Commands;

internal sealed class UpdatePackCommandHandler(
    IFileSystem fileSystem,
    PackUpdateService packUpdateService,
    LinkCommandDispatcher linkCommandDispatcher,
    PackUpdateSelectionService updateSelectionService,
    IPackUpdatePrompter packUpdatePrompter,
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
        var packReferenceArgument = CreatePackReferenceArgument(completionProvider);
        var promptOption = CreatePromptOption();
        var dryRunOption = CreateDryRunOption();
        var noFileChangeOutputOption = CreateNoFileChangeOutputOption();
        var acceptSourcesOption = CreateAcceptSourcesOption();
        var promptParametersOption = CreatePromptParametersOption();
        var skipParametersOption = CreateSkipParametersOption();
        var configurationOptions = CreateConfigurationOptions(completionProvider);
        var scriptsOption = CreateScriptsOption();
        var skipInstructionsOption = CreateSkipInstructionsOption();
        var command = new Command("update", "Update installed packs.")
        {
            packReferenceArgument,
            promptOption,
            dryRunOption,
            noFileChangeOutputOption,
            acceptSourcesOption,
            promptParametersOption,
            skipParametersOption,
            scriptsOption,
            skipInstructionsOption,
        };
        AddConfigurationOptions(command, configurationOptions);
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
                promptParametersOption,
                skipParametersOption,
                configurationOptions.Parameter,
                configurationOptions.NoVariables,
                configurationOptions.SkipVariable,
                configurationOptions.RemapDirectory,
                configurationOptions.RemapFile,
                configurationOptions.SaveRemap,
                scriptsOption,
                skipInstructionsOption
            )
        );

        return command;
    }

    private static void AddConfigurationOptions(
        Command command,
        (
            Option<string[]> Parameter,
            Option<bool> NoVariables,
            Option<string[]> SkipVariable,
            Option<string[]> RemapDirectory,
            Option<string[]> RemapFile,
            Option<bool> SaveRemap
        ) options
    )
    {
        command.Options.Add(options.Parameter);
        command.Options.Add(options.NoVariables);
        command.Options.Add(options.SkipVariable);
        command.Options.Add(options.RemapDirectory);
        command.Options.Add(options.RemapFile);
        command.Options.Add(options.SaveRemap);
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
        Option<bool> promptParametersOption,
        Option<bool> skipParametersOption,
        Option<string[]> parameterOption,
        Option<bool> noVariablesOption,
        Option<string[]> skipVariableOption,
        Option<string[]> remapDirectoryOption,
        Option<string[]> remapFileOption,
        Option<bool> saveRemapOption,
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
        var promptAllParameters = parseResult.GetValue(promptParametersOption);
        var skipParameters = parseResult.GetValue(skipParametersOption);
        if (
            GetParameterPromptOptionError(dryRun, promptAllParameters, skipParameters) is
            { } parameterPromptOptionError
        )
        {
            return console.Fail(parameterPromptOptionError);
        }

        var promptParameters = CreateParameterPrompt(
            promptAllParameters || (dryRun && !skipParameters)
        );
        var updateOptions = CreateUpdateOptions(
            parseResult,
            parameterOption,
            noVariablesOption,
            skipVariableOption,
            remapDirectoryOption,
            remapFileOption,
            saveRemapOption
        );
        var targetRemapping = CreateTargetRemapping(
            workspaceDirectory,
            updateOptions,
            references.Count
        );
        if (targetRemapping.Value is not { } parsedTargetRemapping)
        {
            return console.Fail(targetRemapping.Error);
        }

        var scriptMode = ParseScriptMode(parseResult, scriptsOption);
        if (scriptMode.Value is not { } parsedScriptMode)
        {
            return console.Fail(scriptMode.Error);
        }

        var prompt = parseResult.GetValue(promptOption);
        if (GetPromptOptionError(prompt, references.Count) is { } promptOptionError)
        {
            return console.Fail(promptOptionError);
        }

        return await ExecuteUpdateAsync(
            workspaceDirectory,
            referenceValues,
            references,
            prompt,
            dryRun,
            showFileChanges,
            parsedScriptMode,
            parseResult.GetValue(skipInstructionsOption),
            acceptSources,
            promptParameters,
            updateOptions,
            parsedTargetRemapping
        );
    }

    private ManifestOperationResult<ManagedFileTargetRemapping> CreateTargetRemapping(
        string workspaceDirectory,
        PackUpdateOptions updateOptions,
        int referenceCount
    )
    {
        if (GetRemappingOptionError(updateOptions, referenceCount) is { } error)
        {
            return ManifestOperationResult<ManagedFileTargetRemapping>.Failure(error);
        }

        return ManagedFileTargetRemapping.Create(
            fileSystem,
            workspaceDirectory,
            updateOptions.DirectoryRemappings,
            updateOptions.FileRemappings
        );
    }

    private async Task<int> ExecuteUpdateAsync(
        string workspaceDirectory,
        string[] referenceValues,
        IReadOnlyList<PackReference> references,
        bool prompt,
        bool dryRun,
        bool showFileChanges,
        ScriptExecutionMode scriptMode,
        bool skipInstructions,
        bool acceptSources,
        PackParameterPromptCallback? promptParameters,
        PackUpdateOptions updateOptions,
        ManagedFileTargetRemapping targetRemapping
    )
    {
        if (prompt)
        {
            return await HandleResultAsync(
                await PromptAndUpdateAsync(
                    workspaceDirectory,
                    dryRun,
                    scriptMode,
                    skipInstructions,
                    acceptSources,
                    promptParameters,
                    updateOptions
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
                    scriptMode,
                    skipInstructions,
                    "Updating packs...",
                    acceptSources,
                    promptParameters,
                    updateOptions
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
            scriptMode,
            skipInstructions,
            acceptSources,
            promptParameters,
            updateOptions,
            targetRemapping
        );
    }

    private static ManifestOperationResult<ScriptExecutionMode> ParseScriptMode(
        ParseResult parseResult,
        Option<string?> scriptsOption
    ) =>
        ScriptExecutionMode.Parse(
            parseResult.GetValue(scriptsOption) ?? ScriptExecutionMode.Prompt.Value
        );

    private static string? GetPromptOptionError(bool prompt, int referenceCount) =>
        prompt && referenceCount > 0
            ? "The --prompt option is only available when updating all packs."
            : null;

    private async Task<int> UpdateRequestedPacksAsync(
        string workspaceDirectory,
        string[] referenceValues,
        IReadOnlyList<PackReference> references,
        bool dryRun,
        bool showFileChanges,
        ScriptExecutionMode scriptMode,
        bool skipInstructions,
        bool acceptSources,
        PackParameterPromptCallback? promptParameters,
        PackUpdateOptions updateOptions,
        ManagedFileTargetRemapping targetRemapping
    )
    {
        for (var index = 0; index < referenceValues.Length; index++)
        {
            var referenceValue = referenceValues[index];
            var linkExitCode = await linkCommandDispatcher.TryUpdateAsync(
                workspaceDirectory,
                referenceValue,
                targetRemapping,
                updateOptions.SaveRemapping
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
                    acceptSources,
                    promptParameters,
                    updateOptions
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

    private static Argument<string[]> CreatePackReferenceArgument(
        CliCompletionProvider completionProvider
    )
    {
        var argument = new Argument<string[]>("pack-reference")
        {
            Arity = ArgumentArity.ZeroOrMore,
            Description = "Pack IDs, optionally followed by @version.",
            HelpName = "pack-reference",
        };
        argument.CompletionSources.Add(completionProvider.GetInstalledReferences);
        return argument;
    }

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

    private static Option<bool> CreatePromptParametersOption() =>
        new("--prompt-parameters")
        {
            Description = "Prompt for every configurable pack parameter.",
        };

    private static Option<bool> CreateSkipParametersOption() =>
        new("--skip-parameters")
        {
            Description = "Do not prompt for pack parameters during a dry run.",
        };

    private static Option<string[]> CreateParameterOption() =>
        new("--parameter") { Description = "Template parameter in <name>=<value> form." };

    private static (
        Option<string[]> Parameter,
        Option<bool> NoVariables,
        Option<string[]> SkipVariable,
        Option<string[]> RemapDirectory,
        Option<string[]> RemapFile,
        Option<bool> SaveRemap
    ) CreateConfigurationOptions(CliCompletionProvider completionProvider) =>
        (
            CreateParameterOption(),
            CreateNoVariablesOption(),
            CreateSkipVariableOption(completionProvider),
            CreateRemapDirectoryOption(),
            CreateRemapFileOption(),
            CreateSaveRemapOption()
        );

    private static Option<bool> CreateNoVariablesOption() =>
        new("--no-variables", "-nv") { Description = "Do not bind matching project variables." };

    private static Option<string[]> CreateSkipVariableOption(
        CliCompletionProvider completionProvider
    )
    {
        var option = new Option<string[]>("--skip-variable", "-sv")
        {
            Description = "Project variable name to skip during parameter binding.",
        };
        option.CompletionSources.Add(completionProvider.GetConfiguredVariableNames);
        return option;
    }

    private static Option<string[]> CreateRemapDirectoryOption() =>
        new("--remap-directory")
        {
            Description = "Remap a declared target directory with <source>=<target>.",
        };

    private static Option<string[]> CreateRemapFileOption() =>
        new("--remap-file")
        {
            Description = "Remap a declared target file with <source>=<target>.",
        };

    private static Option<bool> CreateSaveRemapOption() =>
        new("--save-remap") { Description = "Save provided target remappings to lunapack.yml." };

    private static Option<string?> CreateScriptsOption()
    {
        var option = new Option<string?>("--scripts")
        {
            Description = "Lifecycle script mode: prompt, run, or skip.",
        };
        option.CompletionSources.Add("prompt", "run", "skip");
        return option;
    }

    private static Option<bool> CreateSkipInstructionsOption() =>
        new("--skip-instructions") { Description = "Skip lifecycle instructions." };

    private Task<PackUpdateService.UpdateResult> UpdateAsync(
        string projectDirectory,
        PackReference? reference,
        bool dryRun,
        ScriptExecutionMode scriptMode,
        bool skipInstructions,
        string status,
        bool acceptSources,
        PackParameterPromptCallback? promptParameters,
        PackUpdateOptions updateOptions
    ) =>
        scriptMode == ScriptExecutionMode.Prompt
            ? packUpdateService.UpdateAsync(
                projectDirectory,
                reference,
                dryRun,
                scriptMode,
                skipInstructions,
                acceptSources,
                promptParameters,
                updateOptions
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
                        acceptSources,
                        promptParameters,
                        updateOptions
                    )
            );

    private async Task<PackUpdateService.UpdateResult> PromptAndUpdateAsync(
        string projectDirectory,
        bool dryRun,
        ScriptExecutionMode scriptMode,
        bool skipInstructions,
        bool acceptSources,
        PackParameterPromptCallback? promptParameters,
        PackUpdateOptions updateOptions
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
            acceptSources,
            promptParameters,
            updateOptions
        );
    }

    private PackParameterPromptCallback? CreateParameterPrompt(bool enabled)
    {
        Dictionary<string, IReadOnlyList<string>>? promptedParameters = null;
        return enabled ? prompts => PromptForParameters(prompts, promptedParameters ??= []) : null;
    }

    private Dictionary<string, IReadOnlyList<string>> PromptForParameters(
        IReadOnlyList<PackParameterPrompt> prompts,
        IDictionary<string, IReadOnlyList<string>> promptedParameters
    )
    {
        var selected = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        foreach (var prompt in prompts)
        {
            if (!promptedParameters.TryGetValue(prompt.Id, out var values))
            {
                values = console.PromptValues(prompt);
                promptedParameters.Add(prompt.Id, values);
            }

            selected.Add(prompt.Id, values);
        }

        return selected;
    }

    private static string? GetParameterPromptOptionError(
        bool dryRun,
        bool promptParameters,
        bool skipParameters
    ) =>
        skipParameters && !dryRun ? "The --skip-parameters option is only available with --dry-run."
        : skipParameters && promptParameters
            ? "The --skip-parameters and --prompt-parameters options are mutually exclusive."
        : null;

    private static string? GetRemappingOptionError(PackUpdateOptions options, int referenceCount) =>
        options.SaveRemapping && !options.HasRemappings
            ? "--save-remap requires --remap-directory or --remap-file."
        : options.HasRemappings && referenceCount != 1
            ? "Update remapping options require exactly one pack reference."
        : null;

    private static PackUpdateOptions CreateUpdateOptions(
        ParseResult parseResult,
        Option<string[]> parameterOption,
        Option<bool> noVariablesOption,
        Option<string[]> skipVariableOption,
        Option<string[]> remapDirectoryOption,
        Option<string[]> remapFileOption,
        Option<bool> saveRemapOption
    ) =>
        new()
        {
            Parameters = parseResult.GetValue(parameterOption) ?? [],
            NoVariables = parseResult.GetValue(noVariablesOption),
            SkippedVariables = parseResult.GetValue(skipVariableOption) ?? [],
            DirectoryRemappings = parseResult.GetValue(remapDirectoryOption) ?? [],
            FileRemappings = parseResult.GetValue(remapFileOption) ?? [],
            SaveRemapping = parseResult.GetValue(saveRemapOption),
        };

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
            var lines = PackDryRunFormatter.FormatUpdate(
                result.Outcomes,
                result.FileChangePlan ?? new PackUpdatePlan([]),
                result.ProposedSourceSwitch
            );
            foreach (var line in lines)
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

            if (outcome.SourceSelection is not null)
            {
                console.MarkupInfo(
                    PackDryRunFormatter.FormatSourceSelection(outcome.SourceSelection)
                );
            }
        }
    }
}
