using System.CommandLine;
using System.IO.Abstractions;

namespace Lunapack.Cli;

internal sealed class InstallPackCommandHandler(
    IFileSystem fileSystem,
    PackLifecycleService packLifecycleService,
    LinkCommandDispatcher linkCommandDispatcher,
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
            Arity = ArgumentArity.OneOrMore,
            Description = "Pack IDs, optionally followed by @version.",
        };
        var destinationOption = new Option<string?>("--destination", "-d")
        {
            Description = "Directory where the requested pack's files are installed.",
        };
        var remapDirectoryOption = new Option<string[]>("--remap-directory")
        {
            Description = "Remap a declared target directory with <source>=<target>.",
        };
        var remapFileOption = new Option<string[]>("--remap-file")
        {
            Description = "Remap a declared target file with <source>=<target>.",
        };
        var adoptExistingOption = new Option<bool>("--adopt-existing", "-a")
        {
            Description = "Adopt matching existing files for the requested pack.",
        };
        var dryRunOption = new Option<bool>("--dry-run", "-D")
        {
            Description = "Plan the installation without modifying files or state.",
        };
        var acceptSourcesOption = new Option<bool>("--accept-sources")
        {
            Description = "Approve conflict-free external source additions.",
        };
        var parameterOption = new Option<string[]>("--parameter", "-p")
        {
            Description = "Template parameter in <name>=<value> form.",
        };
        var noVariablesOption = new Option<bool>("--no-variables", "-nv")
        {
            Description = "Do not bind matching project variables.",
        };
        var skipVariableOption = new Option<string[]>("--skip-variable", "-sv")
        {
            Description = "Project variable name to skip during parameter binding.",
        };
        var scriptsOption = new Option<string?>("--scripts")
        {
            Description = "Lifecycle script mode: prompt, run, or skip.",
        };
        var command = new Command("install", "Install a pack.")
        {
            packReferenceArgument,
            destinationOption,
            remapDirectoryOption,
            remapFileOption,
            adoptExistingOption,
            dryRunOption,
            acceptSourcesOption,
            parameterOption,
            noVariablesOption,
            skipVariableOption,
            scriptsOption,
        };
        command.SetAction(async parseResult =>
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
                    parseResult.GetValue(adoptExistingOption),
                    parseResult.GetValue(parameterOption) ?? [],
                    parseResult.GetValue(noVariablesOption),
                    parseResult.GetValue(skipVariableOption) ?? [],
                    parsedScriptMode,
                    parseResult.GetValue(dryRunOption),
                    parseResult.GetValue(acceptSourcesOption),
                    skipInstalledRoots: packReferences.Length > 1
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

    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Maintainability",
        "MA0051:Method is too long",
        Justification = "Command request orchestration stays adjacent to CLI input handling; ADR-0040 owns lifecycle execution policy."
    )]
    private async Task<int> InstallAsync(
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
        bool dryRun,
        bool acceptSources,
        bool skipInstalledRoots
    )
    {
        var prerequisiteFailure = await prerequisiteGuard.RequireSourcesAsync(workspaceDirectory);
        if (prerequisiteFailure is not null)
        {
            return prerequisiteFailure.Value;
        }

        var remapping = ManagedFileTargetRemapping.Create(
            fileSystem,
            workspaceDirectory,
            directoryRemappings,
            fileRemappings
        );
        if (remapping.Value is not { } targetRemapping)
        {
            return console.Fail(remapping.Error);
        }

        var linkExitCode = await linkCommandDispatcher.TryInstallAsync(
            workspaceDirectory,
            packReference,
            adoptExisting,
            targetRemapping
        );
        if (linkExitCode is not null)
        {
            return linkExitCode.Value;
        }

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
            scriptMode
        );
        if (installationRequest.Value is not { } request)
        {
            return console.Fail(installationRequest.Error);
        }

        request = request with { AcceptSources = acceptSources };

        var skippedInstall = await WarnWhenRootAlreadyInstalledAsync(
            workspaceDirectory,
            request.PackReference,
            skipInstalledRoots
        );
        if (skippedInstall is not null)
        {
            return skippedInstall.Value;
        }

        var unresolvedParameters = await packLifecycleService.FindUnresolvedRequiredParametersAsync(
            workspaceDirectory,
            request
        );
        if (unresolvedParameters.Value is not { } prompts)
        {
            var exitCode = console.Fail(unresolvedParameters.Error);
            if (unresolvedParameters.ErrorKind == ManifestOperationErrorKind.PackNotFound)
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

        request = PromptForRequiredParameters(request, prompts);
        if (!dryRun)
        {
            var exitCode =
                request.ScriptMode == ScriptExecutionMode.Prompt
                    ? await packLifecycleService.InstallAsync(workspaceDirectory, request)
                    : await console.RunWithStatusAsync(
                        $"Installing {request.PackReference.Id}...",
                        () => packLifecycleService.InstallAsync(workspaceDirectory, request)
                    );
            if (exitCode == 0)
            {
                console.Info($"✓ Installed {request.PackReference.Id}");
                nextStepRenderer.Render(
                    nextStepAdvisor.Recommend(
                        NextStepContext.PackInstalled,
                        request.PackReference.Id
                    )
                );
            }

            return exitCode;
        }

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
            console.Info(line);
        }

        return 0;
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
        ScriptExecutionMode scriptMode
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
            scriptMode
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

    private PackInstallationRequest PromptForRequiredParameters(
        PackInstallationRequest request,
        IReadOnlyList<PackParameterPrompt> prompts
    )
    {
        if (prompts.Count == 0)
        {
            return request;
        }

        var parameters = new Dictionary<string, string>(request.Parameters, StringComparer.Ordinal);
        foreach (var prompt in prompts)
        {
            parameters.Add(prompt.Id, console.Prompt(prompt));
        }

        return request with
        {
            Parameters = parameters,
        };
    }
}
