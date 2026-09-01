using System.CommandLine;
using System.IO.Abstractions;
using Lunapack.Cli.Application;
using Lunapack.Cli.Application.CommandExecution;
using Lunapack.Cli.Application.Guidance;
using Lunapack.Cli.Catalog;
using Lunapack.Cli.Links;
using Lunapack.Cli.Packs.ManagedFiles;
using Lunapack.Cli.Packs.Planning;
using Lunapack.Cli.Project;
using Lunapack.Cli.Trust;

namespace Lunapack.Cli.Packs.Commands;

internal sealed class InstallPackCommandHandler(
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
        var packReferenceArgument = CreatePackReferenceArgument(completionProvider);
        var destinationOption = CreateDestinationOption();
        var remapDirectoryOption = CreateRemapDirectoryOption();
        var remapFileOption = CreateRemapFileOption();
        var saveRemapOption = CreateSaveRemapOption();
        var adoptExistingOption = CreateAdoptExistingOption();
        var dryRunOption = CreateDryRunOption();
        var noFileChangeOutputOption = CreateNoFileChangeOutputOption();
        var acceptSourcesOption = CreateAcceptSourcesOption();
        var parameterOption = CreateParameterOption();
        var promptParametersOption = CreatePromptParametersOption();
        var noVariablesOption = CreateNoVariablesOption();
        var skipVariableOption = CreateSkipVariableOption(completionProvider);
        var scriptsOption = CreateScriptsOption();
        var skipInstructionsOption = CreateSkipInstructionsOption();
        var command = new Command("install", "Install a pack.")
        {
            packReferenceArgument,
            destinationOption,
            remapDirectoryOption,
            remapFileOption,
            saveRemapOption,
            adoptExistingOption,
            dryRunOption,
            noFileChangeOutputOption,
            acceptSourcesOption,
            parameterOption,
            promptParametersOption,
            noVariablesOption,
            skipVariableOption,
            scriptsOption,
            skipInstructionsOption,
        };
        command.SetAction(parseResult =>
            ExecuteCommandAsync(
                projectDirectory,
                workspaceOption,
                parseResult,
                packReferenceArgument,
                destinationOption,
                remapDirectoryOption,
                remapFileOption,
                saveRemapOption,
                adoptExistingOption,
                dryRunOption,
                noFileChangeOutputOption,
                acceptSourcesOption,
                parameterOption,
                promptParametersOption,
                noVariablesOption,
                skipVariableOption,
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
        Option<string?> destinationOption,
        Option<string[]> remapDirectoryOption,
        Option<string[]> remapFileOption,
        Option<bool> saveRemapOption,
        Option<bool> adoptExistingOption,
        Option<bool> dryRunOption,
        Option<bool> noFileChangeOutputOption,
        Option<bool> acceptSourcesOption,
        Option<string[]> parameterOption,
        Option<bool> promptParametersOption,
        Option<bool> noVariablesOption,
        Option<string[]> skipVariableOption,
        Option<string?> scriptsOption,
        Option<bool> skipInstructionsOption
    )
    {
        var packReferences = parseResult.GetValue(packReferenceArgument) ?? [];
        if (packReferences.Length == 0)
        {
            return console.Fail("A pack ID is required.");
        }

        var scriptMode = ScriptExecutionMode.Parse(
            parseResult.GetValue(scriptsOption) ?? ScriptExecutionMode.Prompt.Value
        );
        if (scriptMode.Value is not { } parsedScriptMode)
        {
            return console.Fail(scriptMode.Error);
        }

        var workspaceDirectory = workspaceDirectoryResolver.Resolve(
            projectDirectory,
            parseResult.GetValue(workspaceOption)
        );
        foreach (var packReference in packReferences)
        {
            var exitCode = await InstallAsync(
                workspaceDirectory,
                packReference,
                parseResult.GetValue(destinationOption),
                parseResult.GetValue(remapDirectoryOption) ?? [],
                parseResult.GetValue(remapFileOption) ?? [],
                parseResult.GetValue(saveRemapOption),
                parseResult.GetValue(adoptExistingOption),
                parseResult.GetValue(parameterOption) ?? [],
                parseResult.GetValue(promptParametersOption),
                parseResult.GetValue(noVariablesOption),
                parseResult.GetValue(skipVariableOption) ?? [],
                parsedScriptMode,
                parseResult.GetValue(skipInstructionsOption),
                parseResult.GetValue(dryRunOption),
                parseResult.GetValue(noFileChangeOutputOption),
                parseResult.GetValue(acceptSourcesOption),
                skipInstalledRoots: packReferences.Length > 1
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
            Arity = ArgumentArity.OneOrMore,
            Description = "Pack IDs, optionally followed by @version.",
            HelpName = "pack-reference",
        };
        argument.CompletionSources.Add(completionProvider.GetInstallReferences);
        return argument;
    }

    private static Option<string?> CreateDestinationOption() =>
        new("--destination", "-d")
        {
            Description = "Directory where the requested pack's files are installed.",
        };

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

    private static Option<bool> CreateAdoptExistingOption() =>
        new("--adopt-existing", "-a")
        {
            Description = "Adopt matching existing files for the requested pack.",
        };

    private static Option<bool> CreateDryRunOption() =>
        new("--dry-run", "-D")
        {
            Description = "Plan the installation without modifying files or state.",
        };

    private static Option<bool> CreateNoFileChangeOutputOption() =>
        new("--no-file-change-output")
        {
            Description = "Do not list managed-file changes after installation.",
        };

    private static Option<bool> CreateAcceptSourcesOption() =>
        new("--accept-sources")
        {
            Description = "Approve conflict-free external source additions.",
        };

    private static Option<string[]> CreateParameterOption() =>
        new("--parameter", "-p") { Description = "Template parameter in <name>=<value> form." };

    private static Option<bool> CreatePromptParametersOption() =>
        new("--prompt-parameters")
        {
            Description = "Prompt for every configurable pack parameter.",
        };

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

    private async Task<int> InstallAsync(
        string workspaceDirectory,
        string packReference,
        string? destination,
        string[] directoryRemappings,
        string[] fileRemappings,
        bool saveRemapping,
        bool adoptExisting,
        string[] parameters,
        bool promptParameters,
        bool noVariables,
        string[] skippedVariables,
        ScriptExecutionMode scriptMode,
        bool skipInstructions,
        bool dryRun,
        bool noFileChangeOutput,
        bool acceptSources,
        bool skipInstalledRoots
    )
    {
        var prerequisiteFailure = await prerequisiteGuard.RequireSourcesAsync(workspaceDirectory);
        if (prerequisiteFailure is not null)
        {
            return prerequisiteFailure.Value;
        }

        var remapping = CreateTargetRemapping(
            workspaceDirectory,
            directoryRemappings,
            fileRemappings,
            saveRemapping
        );
        if (remapping.Value is not { } targetRemapping)
        {
            return console.Fail(remapping.Error);
        }

        var linkExitCode = await linkCommandDispatcher.TryInstallAsync(
            workspaceDirectory,
            packReference,
            adoptExisting,
            targetRemapping,
            saveRemapping
        );
        if (linkExitCode is not null)
        {
            return linkExitCode.Value;
        }

        return await PrepareAndInstallPackAsync(
            workspaceDirectory,
            packReference,
            destination,
            directoryRemappings,
            fileRemappings,
            saveRemapping,
            adoptExisting,
            parameters,
            promptParameters,
            noVariables,
            skippedVariables,
            scriptMode,
            skipInstructions,
            dryRun,
            noFileChangeOutput,
            acceptSources,
            skipInstalledRoots
        );
    }

    private ManifestOperationResult<ManagedFileTargetRemapping> CreateTargetRemapping(
        string workspaceDirectory,
        string[] directoryRemappings,
        string[] fileRemappings,
        bool saveRemapping
    )
    {
        var remapping = ManagedFileTargetRemapping.Create(
            fileSystem,
            workspaceDirectory,
            directoryRemappings,
            fileRemappings
        );
        if (remapping.Value is not { } targetRemapping)
        {
            return remapping;
        }

        if (saveRemapping && !targetRemapping.HasMappings)
        {
            return ManifestOperationResult<ManagedFileTargetRemapping>.Failure(
                "--save-remap requires --remap-directory or --remap-file."
            );
        }

        return remapping;
    }

    private async Task<int> PrepareAndInstallPackAsync(
        string workspaceDirectory,
        string packReference,
        string? destination,
        string[] directoryRemappings,
        string[] fileRemappings,
        bool saveRemapping,
        bool adoptExisting,
        string[] parameters,
        bool promptParameters,
        bool noVariables,
        string[] skippedVariables,
        ScriptExecutionMode scriptMode,
        bool skipInstructions,
        bool dryRun,
        bool noFileChangeOutput,
        bool acceptSources,
        bool skipInstalledRoots
    )
    {
        var installationRequest = CreateInstallationRequest(
            workspaceDirectory,
            packReference,
            destination,
            directoryRemappings,
            fileRemappings,
            adoptExisting,
            parameters,
            noVariables,
            skippedVariables,
            scriptMode,
            skipInstructions,
            saveRemapping
        );
        if (installationRequest.Value is not { } request)
        {
            return console.Fail(installationRequest.Error);
        }

        request = request with { AcceptSources = acceptSources };
        return await RunPreparedInstallAsync(
            workspaceDirectory,
            request,
            dryRun,
            noFileChangeOutput,
            skipInstalledRoots,
            promptParameters || dryRun
        );
    }

    private async Task<int> RunPreparedInstallAsync(
        string workspaceDirectory,
        PackInstallationRequest request,
        bool dryRun,
        bool noFileChangeOutput,
        bool skipInstalledRoots,
        bool promptParameters
    )
    {
        var skippedInstall = await WarnWhenRootAlreadyInstalledAsync(
            workspaceDirectory,
            request.PackReference,
            skipInstalledRoots
        );
        if (skippedInstall is not null)
        {
            return skippedInstall.Value;
        }

        var promptedParameters = await packLifecycleService.PromptInstallParametersAsync(
            workspaceDirectory,
            request,
            promptParameters,
            PromptForParameters
        );
        if (promptedParameters.Value is not { } parameters)
        {
            var exitCode = console.Fail(promptedParameters.Error);
            if (promptedParameters.ErrorKind == ManifestOperationErrorKind.PackNotFound)
            {
                nextStepRenderer.Render(
                    nextStepAdvisor.Recommend(
                        NextStepContext.PackNotFound,
                        request.PackReference.Id
                    ),
                    "Try:"
                );
            }

            return exitCode;
        }

        request = request with { ParameterValues = parameters };
        return dryRun
            ? await PreviewInstallAsync(workspaceDirectory, request)
            : await ExecuteInstallAsync(workspaceDirectory, request, noFileChangeOutput);
    }

    private async Task<int> ExecuteInstallAsync(
        string workspaceDirectory,
        PackInstallationRequest request,
        bool noFileChangeOutput
    )
    {
        TimeSpan? managedFileChangesDuration = null;
        PackUpdatePlan? appliedPlan = null;
        PackSourceSelection? sourceSelection = null;
        var exitCode =
            request.ScriptMode == ScriptExecutionMode.Prompt
                ? await packLifecycleService.InstallAsync(
                    workspaceDirectory,
                    request,
                    duration => managedFileChangesDuration = duration,
                    plan => appliedPlan = plan,
                    selection => sourceSelection = selection
                )
                : await console.RunWithStatusAsync(
                    $"Installing {request.PackReference.Id}...",
                    () =>
                        packLifecycleService.InstallAsync(
                            workspaceDirectory,
                            request,
                            duration => managedFileChangesDuration = duration,
                            plan => appliedPlan = plan,
                            selection => sourceSelection = selection
                        )
                );
        if (exitCode == 0)
        {
            var installedVersion = await packLifecycleService.GetInstalledVersionAsync(
                workspaceDirectory,
                request.PackReference.Id
            );
            if (installedVersion.Value is not { } version)
            {
                return console.Fail(installedVersion.Error);
            }

            console.Info(string.Empty);
            console.Success(
                $"Installed '{request.PackReference.Id}' (version '{version}') in {CliDuration.Format(managedFileChangesDuration ?? TimeSpan.Zero)}"
            );
            if (sourceSelection is not null)
            {
                console.MarkupInfo(PackDryRunFormatter.FormatSourceSelection(sourceSelection));
            }
            if (!noFileChangeOutput && appliedPlan is not null)
            {
                WriteMarkup(PackDryRunFormatter.FormatAppliedFileChanges(appliedPlan));
            }
            nextStepRenderer.Render(
                nextStepAdvisor.Recommend(NextStepContext.PackInstalled, request.PackReference.Id)
            );
        }

        return exitCode;
    }

    private async Task<int> PreviewInstallAsync(
        string workspaceDirectory,
        PackInstallationRequest request
    )
    {
        var plannedInstall = await packLifecycleService.DryRunInstallAsync(
            workspaceDirectory,
            request
        );
        if (plannedInstall.Value is not { } preview)
        {
            return console.Fail(plannedInstall.Error);
        }

        foreach (var line in PackDryRunFormatter.FormatInstall(preview))
        {
            console.MarkupInfo(line);
        }

        return 0;
    }

    private void WriteMarkup(IEnumerable<string> lines)
    {
        foreach (var line in lines)
        {
            console.MarkupInfo(line);
        }
    }

    private ManifestOperationResult<PackInstallationRequest> CreateInstallationRequest(
        string workspaceDirectory,
        string packReference,
        string? destination,
        string[] directoryRemappings,
        string[] fileRemappings,
        bool adoptExisting,
        string[] parameters,
        bool noVariables,
        string[] skippedVariables,
        ScriptExecutionMode scriptMode,
        bool skipInstructions,
        bool saveRemapping
    ) =>
        PackInstallationRequest.Create(
            fileSystem,
            workspaceDirectory,
            packReference,
            destination,
            adoptExisting,
            parameters,
            noVariables,
            skippedVariables,
            directoryRemappings,
            fileRemappings,
            scriptMode,
            skipInstructions,
            saveRemapping
        );

    private async Task<int?> WarnWhenRootAlreadyInstalledAsync(
        string workspaceDirectory,
        PackReference packReference,
        bool skipInstalledRoots
    )
    {
        if (!skipInstalledRoots)
        {
            return null;
        }

        var installed = await packLifecycleService.IsRequestedRootInstalledAsync(
            workspaceDirectory,
            packReference
        );
        if (!installed.IsSuccess)
        {
            return console.Fail(installed.Error);
        }

        if (!installed.Value)
        {
            return null;
        }

        console.Warning($"Pack '{packReference.Id}' is already installed.");
        return 0;
    }

    private IReadOnlyDictionary<string, IReadOnlyList<string>> PromptForParameters(
        IReadOnlyList<PackParameterPrompt> prompts
    )
    {
        return prompts.ToDictionary(
            prompt => prompt.Id,
            prompt => console.PromptValues(prompt),
            StringComparer.Ordinal
        );
    }
}
