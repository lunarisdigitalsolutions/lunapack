using System.CommandLine;
using System.IO.Abstractions;
using Lunapack.Cli.Application;
using Lunapack.Cli.Application.CommandExecution;
using Lunapack.Cli.Application.Guidance;
using Lunapack.Cli.Application.Paths;
using Lunapack.Cli.Packs.ManagedFiles;
using Lunapack.Cli.Packs.Manifest;
using Lunapack.Cli.Sources;
using Lunapack.Cli.Sources.Git;

namespace Lunapack.Cli.Packs.Authoring;

internal sealed class PackAuthoringCommandHandler(
    IFileSystem fileSystem,
    PackManifestStore manifestStore,
    WorkspaceDirectoryResolver workspaceDirectoryResolver,
    NextStepAdvisor nextStepAdvisor,
    NextStepRenderer nextStepRenderer,
    CliConsole console,
    GitRefResolver? gitRefResolver,
    PackAuthoringValidationService validationService
)
{
    private static readonly string[] _hooks =
    [
        "preInstall",
        "postInstall",
        "preUpdate",
        "postUpdate",
        "preUninstall",
        "postUninstall",
    ];

    public Command CreateCommand(string projectDirectory, Option<string?> workspaceOption)
    {
        var command = new Command("pack", "Author a local pack manifest.")
        {
            CreateInitCommand(projectDirectory, workspaceOption),
            CreateAddCommand(projectDirectory, workspaceOption),
            CreateSetCommand(projectDirectory, workspaceOption),
            CreateRemoveCommand(projectDirectory, workspaceOption),
            CreateDisplayCommand("list", projectDirectory, workspaceOption),
            CreateDisplayCommand("show", projectDirectory, workspaceOption),
            CreateDisplayCommand("hooks", projectDirectory, workspaceOption),
            CreateDisplayCommand("sources", projectDirectory, workspaceOption),
            CreateValidateCommand(projectDirectory, workspaceOption),
        };
        command.SetAction(parseResult =>
        {
            var workspace = ResolveWorkspace(parseResult, projectDirectory, workspaceOption);
            var manifestPath = fileSystem.Path.Combine(workspace, PackManifestStore.FileName);
            RenderNextSteps(
                fileSystem.File.Exists(manifestPath)
                    ? NextStepContext.PackManifestPresent
                    : NextStepContext.PackManifestMissing
            );
            return 0;
        });
        return command;
    }

    private Command CreateInitCommand(string projectDirectory, Option<string?> workspaceOption)
    {
        var idOption = new Option<string?>("--id") { Description = "Pack ID." };
        var versionOption = new Option<string?>("--version") { Description = "Semantic version." };
        var authorOption = new Option<string?>("--author") { Description = "Pack author." };
        var licenseOption = new Option<string?>("--license") { Description = "Pack license." };
        var command = new Command("init", "Create a pack manifest.")
        {
            idOption,
            versionOption,
            authorOption,
            licenseOption,
        };
        command.SetAction(async parseResult =>
        {
            var id = GetPackId(parseResult.GetValue(idOption));
            if (id is null)
            {
                return 1;
            }

            var author = GetRequiredValue(
                parseResult.GetValue(authorOption),
                "--author",
                "Pack author:"
            );
            if (author is null)
            {
                return 1;
            }

            var license = GetRequiredValue(
                parseResult.GetValue(licenseOption),
                "--license",
                "Pack license:",
                "MIT"
            );
            if (license is null)
            {
                return 1;
            }

            var version = parseResult.GetValue(versionOption) ?? "1.0.0";
            var result = await manifestStore.CreateAsync(
                ResolveWorkspace(parseResult, projectDirectory, workspaceOption),
                new PackManifest
                {
                    Id = id,
                    Version = version,
                    Author = author,
                    License = license,
                }
            );
            if (result.Value is null)
            {
                return console.Fail(result.Error);
            }

            console.Info("Pack created.");
            RenderNextSteps(NextStepContext.PackInitialized);
            return 0;
        });
        return command;
    }

    private string? GetRequiredValue(
        string? value,
        string optionName,
        string prompt,
        string? defaultValue = null
    )
    {
        if (!string.IsNullOrEmpty(value))
        {
            return value;
        }

        if (console.IsInteractive)
        {
            var promptedValue = console.PromptText(prompt, defaultValue);
            return string.IsNullOrEmpty(promptedValue) ? defaultValue : promptedValue;
        }

        console.Fail($"Missing required option '{optionName}'.");
        return null;
    }

    private string? GetPackId(string? value)
    {
        if (!string.IsNullOrEmpty(value))
        {
            return ValidatePackId(value) ? value : null;
        }

        if (!console.IsInteractive)
        {
            console.Fail("Missing required option '--id'.");
            return null;
        }

        while (true)
        {
            var promptedValue = console.PromptText("Pack ID:");
            if (ValidatePackId(promptedValue))
            {
                return promptedValue;
            }
        }
    }

    private bool ValidatePackId(string value)
    {
        if (ManifestModelValidator.IsPackId(value))
        {
            return true;
        }

        console.Error($"Pack ID '{value}' must use hyphen-separated alphanumeric segments.");
        return false;
    }

    private Command CreateAddCommand(string projectDirectory, Option<string?> workspaceOption)
    {
        return new Command("add", "Add pack content.")
        {
            CreateManagedFileCommand("file", projectDirectory, workspaceOption),
            CreateManagedFileCommand("directory", projectDirectory, workspaceOption),
            CreateManagedFileCommand("glob", projectDirectory, workspaceOption),
            CreateHookCommand(projectDirectory, workspaceOption),
            CreatePackSourceCommand(projectDirectory, workspaceOption),
            CreateReferenceCommand("reference", projectDirectory, workspaceOption, false),
            CreateTagCommand("tag", projectDirectory, workspaceOption, false),
        };
    }

    private Command CreateManagedFileCommand(
        string selectorKind,
        string projectDirectory,
        Option<string?> workspaceOption
    )
    {
        var pathArgument = new Argument<string>("path") { Description = $"{selectorKind} path." };
        var targetOption = new Option<string?>("--target", "-t")
        {
            Description = "Managed target path.",
        };
        var strategyOption = new Option<string?>("--strategy", "-s")
        {
            Description = "Strategy as <type>:<method>.",
        };
        var templateOption = new Option<bool>("--template")
        {
            Description = "Render with Scriban.",
        };
        var conditionOption = new Option<string?>("--condition", "-c")
        {
            Description = "Managed-file condition.",
        };
        var sourceOption = new Option<string?>("--source")
        {
            Description = "Pack-local source alias declared in this manifest.",
        };
        var excludeOption = new Option<string[]>("--exclude")
        {
            Description = "Exclusion pattern relative to the selector root.",
            Arity = ArgumentArity.OneOrMore,
            AllowMultipleArgumentsPerToken = false,
        };
        var flattenOption = new Option<bool>("--flatten")
        {
            Description = "Place every selected file directly under the target directory.",
        };
        var command = new Command(selectorKind, $"Add a managed {selectorKind}.")
        {
            pathArgument,
            targetOption,
            strategyOption,
            templateOption,
            conditionOption,
            sourceOption,
            excludeOption,
            flattenOption,
        };
        command.SetAction(async parseResult =>
            await AddManagedFileAsync(
                ResolveWorkspace(parseResult, projectDirectory, workspaceOption),
                new ManagedFileRequest(
                    selectorKind,
                    parseResult.GetValue(pathArgument),
                    parseResult.GetValue(targetOption),
                    parseResult.GetValue(strategyOption),
                    parseResult.GetValue(templateOption),
                    parseResult.GetValue(conditionOption),
                    parseResult.GetValue(sourceOption),
                    parseResult.GetValue(excludeOption) ?? [],
                    parseResult.GetValue(flattenOption)
                )
            )
        );
        return command;
    }

    private sealed record ManagedFileRequest(
        string SelectorKind,
        string? RawPath,
        string? RawTarget,
        string? RawStrategy,
        bool Template,
        string? Condition,
        string? SourceAlias,
        IReadOnlyList<string> Exclusions,
        bool Flatten
    );

    private async Task<int> AddManagedFileAsync(string workspace, ManagedFileRequest request)
    {
        var prepared = PrepareManagedSelector(workspace, request);
        if (prepared.Value is not { } values)
        {
            return console.Fail(prepared.Error);
        }

        var result = await manifestStore.UpdateAsync(
            workspace,
            manifest => AddManagedSelector(manifest, values)
        );
        if (
            !result.IsSuccess
            && request.SourceAlias is { } missingAlias
            && result.Error?.Contains("is not declared", StringComparison.Ordinal) is true
        )
        {
            var exitCode = console.Fail(result.Error);
            RenderNextSteps(NextStepContext.UnknownPackSourceAlias, missingAlias);
            return exitCode;
        }

        return ReportMutation(result, $"Added {request.SelectorKind} '{values.Selector}'.");
    }

    private ManifestOperationResult<ManagedSelectorValues> PrepareManagedSelector(
        string workspace,
        ManagedFileRequest request
    )
    {
        var selectorKind = request.SelectorKind;
        var selector = NormalizeManagedSelector(workspace, request);
        if (selector.Value is not { } normalizedSelector)
        {
            return ManifestOperationResult<ManagedSelectorValues>.Failure(
                selector.Error ?? "Unable to normalize managed selector."
            );
        }

        var targetValue =
            request.RawTarget
            ?? (
                string.Equals(selectorKind, "glob", StringComparison.Ordinal)
                    ? DeriveGlobTarget(normalizedSelector)
                    : normalizedSelector
            );
        if (targetValue is null)
        {
            return ManifestOperationResult<ManagedSelectorValues>.Failure(
                "Glob target cannot be inferred; provide '--target'."
            );
        }

        var target = ProjectPath.NormalizeProjectRelativePath(fileSystem, workspace, targetValue);
        var strategy = ParseStrategy(request.RawStrategy);
        if (target.Value is not { } normalizedTarget || strategy.Value is not { } parsedStrategy)
        {
            return ManifestOperationResult<ManagedSelectorValues>.Failure(
                target.Error ?? strategy.Error ?? "Unable to prepare managed selector."
            );
        }

        var exclusions = NormalizeExclusions(request.Exclusions);
        if (exclusions.Value is not { } normalizedExclusions)
        {
            return ManifestOperationResult<ManagedSelectorValues>.Failure(
                exclusions.Error ?? "Unable to normalize exclusions."
            );
        }

        return ManifestOperationResult<ManagedSelectorValues>.Success(
            new ManagedSelectorValues(
                selectorKind,
                normalizedSelector,
                normalizedTarget,
                parsedStrategy,
                request.Template,
                request.Condition,
                request.SourceAlias,
                normalizedExclusions,
                request.Flatten
            )
        );
    }

    private ManifestOperationResult<string> NormalizeManagedSelector(
        string workspace,
        ManagedFileRequest request
    )
    {
        var selectorKind = request.SelectorKind;
        if (request.RawPath is null)
        {
            return ManifestOperationResult<string>.Failure($"A {selectorKind} path is required.");
        }

        var fileSelectorCannotFlatten =
            request.Flatten && string.Equals(selectorKind, "file", StringComparison.Ordinal);
        if (fileSelectorCannotFlatten)
        {
            return ManifestOperationResult<string>.Failure(
                "'--flatten' applies to directory and glob selectors only."
            );
        }

        var fileSelectorCannotHaveExclusions =
            request.Exclusions.Count > 0
            && string.Equals(selectorKind, "file", StringComparison.Ordinal);
        if (fileSelectorCannotHaveExclusions)
        {
            return ManifestOperationResult<string>.Failure(
                "'--exclude' applies to directory and glob selectors only."
            );
        }

        var selector = request.SourceAlias is null
            ? NormalizeSelector(workspace, selectorKind, request.RawPath)
            : NormalizeExternalSelector(selectorKind, request.RawPath);
        if (selector.Value is not { } normalizedSelector)
        {
            return ManifestOperationResult<string>.Failure(
                selector.Error ?? "Unable to normalize managed selector."
            );
        }

        return ManifestOperationResult<string>.Success(normalizedSelector);
    }

    private static ManifestOperationResult<IReadOnlyList<string>> NormalizeExclusions(
        IReadOnlyList<string> exclusions
    )
    {
        var normalizedExclusions = new List<string>(exclusions.Count);
        foreach (var exclusion in exclusions)
        {
            var normalized = ProjectPath.Normalize(exclusion);
            var isInvalidExclusion =
                normalized.Length == 0
                || normalized.StartsWith('/')
                || normalized.Split('/').Contains("..", StringComparer.Ordinal);
            if (isInvalidExclusion)
            {
                return ManifestOperationResult<IReadOnlyList<string>>.Failure(
                    $"Exclusion '{exclusion}' must be a non-empty relative pattern."
                );
            }

            normalizedExclusions.Add(normalized);
        }

        return ManifestOperationResult<IReadOnlyList<string>>.Success(normalizedExclusions);
    }

    private sealed record ManagedSelectorValues(
        string SelectorKind,
        string Selector,
        string Target,
        PackManifest.PackManagedFileStrategy Strategy,
        bool Template,
        string? Condition,
        string? SourceAlias,
        IReadOnlyList<string> Exclusions,
        bool Flatten
    );

    private static string? AddManagedSelector(PackManifest manifest, ManagedSelectorValues values)
    {
        if (values.SourceAlias is { } alias && !manifest.Sources.ContainsKey(alias))
        {
            return $"Pack source alias '{alias}' is not declared. Run 'luna pack add source git {alias} <repository-url> --ref <ref>' first.";
        }

        var selectorAlreadyExists = manifest.ManagedFiles.Any(file =>
            string.Equals(GetSelector(file), values.Selector, StringComparison.Ordinal)
            && string.Equals(
                GetSelectorSourceAlias(file),
                values.SourceAlias,
                StringComparison.Ordinal
            )
        );
        if (selectorAlreadyExists)
        {
            return $"Managed selector '{values.Selector}' already exists.";
        }

        var managedFile = new PackManifest.PackManagedFile
        {
            Target = values.Target,
            Strategy = values.Strategy,
            Template = values.Template,
            Condition = values.Condition,
            Exclude = [.. values.Exclusions],
            Flatten = values.Flatten,
        };
        SetSelector(managedFile, values.SelectorKind, values.Selector, values.SourceAlias);
        manifest.ManagedFiles.Add(managedFile);
        return null;
    }

    private Command CreateHookCommand(string projectDirectory, Option<string?> workspaceOption) =>
        new("hook", "Add a lifecycle hook.")
        {
            CreateScriptCommand(projectDirectory, workspaceOption),
            CreateInstructionCommand(projectDirectory, workspaceOption),
        };

    private Command CreateScriptCommand(string projectDirectory, Option<string?> workspaceOption)
    {
        return new Command("script", "Add a lifecycle script.")
        {
            CreateCommandScriptCommand(projectDirectory, workspaceOption),
            CreateFileScriptCommand(projectDirectory, workspaceOption),
        };
    }

    private Command CreateCommandScriptCommand(
        string projectDirectory,
        Option<string?> workspaceOption
    )
    {
        var hookArgument = new Argument<string>("event");
        hookArgument.CompletionSources.Add(_hooks);
        var executableArgument = new Argument<string>("command");
        var argumentsArgument = new Argument<string[]>("arguments")
        {
            Arity = ArgumentArity.ZeroOrMore,
        };
        var descriptionOption = new Option<string?>("--description", "-d");
        var conditionOption = new Option<string?>("--condition", "-c")
        {
            Description = "Include the hook only when the parameter condition is true.",
        };
        var replaceOption = new Option<int?>("--replace")
        {
            Description = "One-based event position to replace.",
        };
        var command = new Command("command", "Add a command-form lifecycle script.")
        {
            hookArgument,
            executableArgument,
            argumentsArgument,
            descriptionOption,
            conditionOption,
            replaceOption,
        };
        command.SetAction(async parseResult =>
        {
            var hook = parseResult.GetValue(hookArgument);
            var executable = parseResult.GetValue(executableArgument);
            if (hook is null || !IsHook(hook) || string.IsNullOrEmpty(executable))
            {
                return console.Fail("A supported hook and non-empty command are required.");
            }

            return await SetHookAsync(
                ResolveWorkspace(parseResult, projectDirectory, workspaceOption),
                hook,
                new PackManifest.PackHook
                {
                    Type = "script",
                    Command = executable,
                    Arguments = [.. parseResult.GetValue(argumentsArgument) ?? []],
                    Condition = parseResult.GetValue(conditionOption),
                    Description = parseResult.GetValue(descriptionOption),
                },
                parseResult.GetValue(replaceOption)
            );
        });
        return command;
    }

    private Command CreateFileScriptCommand(
        string projectDirectory,
        Option<string?> workspaceOption
    )
    {
        var hookArgument = new Argument<string>("event");
        hookArgument.CompletionSources.Add(_hooks);
        var fileArgument = new Argument<string>("file");
        var runnerArgument = new Argument<string>("runner");
        var argumentsArgument = new Argument<string[]>("arguments")
        {
            Arity = ArgumentArity.ZeroOrMore,
        };
        var descriptionOption = new Option<string?>("--description", "-d");
        var conditionOption = new Option<string?>("--condition", "-c")
        {
            Description = "Include the hook only when the parameter condition is true.",
        };
        var replaceOption = new Option<int?>("--replace")
        {
            Description = "One-based event position to replace.",
        };
        var command = new Command("file", "Add a file-form lifecycle script.")
        {
            hookArgument,
            fileArgument,
            runnerArgument,
            argumentsArgument,
            descriptionOption,
            conditionOption,
            replaceOption,
        };
        command.SetAction(async parseResult =>
        {
            var hook = parseResult.GetValue(hookArgument);
            var runner = parseResult.GetValue(runnerArgument);
            var workspace = ResolveWorkspace(parseResult, projectDirectory, workspaceOption);
            var file = ProjectPath.NormalizeProjectRelativePath(
                fileSystem,
                workspace,
                parseResult.GetValue(fileArgument) ?? string.Empty
            );
            if (
                hook is null
                || !IsHook(hook)
                || string.IsNullOrEmpty(runner)
                || file.Value is not { } normalizedFile
            )
            {
                return console.Fail(file.Error ?? "A supported hook and runner are required.");
            }

            return await SetHookAsync(
                workspace,
                hook,
                new PackManifest.PackHook
                {
                    Type = "script",
                    File = normalizedFile,
                    Runner = runner,
                    Arguments = [.. parseResult.GetValue(argumentsArgument) ?? []],
                    Condition = parseResult.GetValue(conditionOption),
                    Description = parseResult.GetValue(descriptionOption),
                },
                parseResult.GetValue(replaceOption)
            );
        });
        return command;
    }

    private Command CreateInstructionCommand(
        string projectDirectory,
        Option<string?> workspaceOption
    )
    {
        var hookArgument = new Argument<string>("event");
        hookArgument.CompletionSources.Add(_hooks);
        var fileArgument = new Argument<string>("file");
        var templatingOption = new Option<bool>("--templating")
        {
            Description = "Render instruction content with Scriban.",
        };
        var conditionOption = new Option<string?>("--condition", "-c")
        {
            Description = "Include the hook only when the parameter condition is true.",
        };
        var replaceOption = new Option<int?>("--replace")
        {
            Description = "One-based event position to replace.",
        };
        var command = new Command("instruction", "Add an instruction lifecycle hook.")
        {
            hookArgument,
            fileArgument,
            templatingOption,
            conditionOption,
            replaceOption,
        };
        command.SetAction(async parseResult =>
        {
            var hook = parseResult.GetValue(hookArgument);
            var workspace = ResolveWorkspace(parseResult, projectDirectory, workspaceOption);
            var file = ProjectPath.NormalizeProjectRelativePath(
                fileSystem,
                workspace,
                parseResult.GetValue(fileArgument) ?? string.Empty
            );
            if (hook is null || !IsHook(hook) || file.Value is not { } normalizedFile)
            {
                return console.Fail(
                    file.Error ?? "A supported hook and instruction file are required."
                );
            }

            return await SetHookAsync(
                workspace,
                hook,
                new PackManifest.PackHook
                {
                    Type = "instruction",
                    File = normalizedFile,
                    Condition = parseResult.GetValue(conditionOption),
                    Templating = parseResult.GetValue(templatingOption),
                },
                parseResult.GetValue(replaceOption)
            );
        });
        return command;
    }

    private async Task<int> SetHookAsync(
        string workspace,
        string hook,
        PackManifest.PackHook declaration,
        int? replacePosition
    )
    {
        var result = await manifestStore.UpdateAsync(
            workspace,
            manifest =>
            {
                manifest.Hooks ??= new PackManifest.PackHooks();
                var declarations = GetHooks(manifest.Hooks, hook);
                if (declarations is null)
                {
                    declarations = [];
                    SetHooks(manifest.Hooks, hook, declarations);
                }

                if (replacePosition is not { } position)
                {
                    declarations.Add(declaration);
                    return null;
                }

                if (position <= 0 || position > declarations.Count)
                {
                    return $"Lifecycle hook position '{position}' does not exist in '{hook}'.";
                }

                declarations[position - 1] = declaration;
                return null;
            }
        );
        return ReportMutation(result, $"Set lifecycle hook '{hook}'.");
    }

    private Command CreateSetCommand(string projectDirectory, Option<string?> workspaceOption)
    {
        var propertyArgument = new Argument<string>("property");
        propertyArgument.CompletionSources.Add(
            "author",
            "description",
            "homepage",
            "id",
            "license",
            "name",
            "version"
        );
        var valueArgument = new Argument<string>("value");
        var command = new Command("set", "Set pack metadata.") { propertyArgument, valueArgument };
        command.SetAction(async parseResult =>
        {
            var property = parseResult.GetValue(propertyArgument);
            var value = parseResult.GetValue(valueArgument);
            if (property is null || value is null)
            {
                return console.Fail("A property and value are required.");
            }

            var result = await manifestStore.UpdateAsync(
                ResolveWorkspace(parseResult, projectDirectory, workspaceOption),
                manifest => SetMetadata(manifest, property, value)
            );
            return ReportMutation(result, $"Set '{property}'.");
        });
        command.Add(CreateReferenceCommand("reference", projectDirectory, workspaceOption, true));
        command.Add(CreateParameterCommand(projectDirectory, workspaceOption));
        return command;
    }

    private Command CreateReferenceCommand(
        string name,
        string projectDirectory,
        Option<string?> workspaceOption,
        bool replaceByDefault
    )
    {
        var idArgument = new Argument<string>("id");
        var versionArgument = new Argument<string>("version");
        var parameterOption = new Option<string[]>("--parameter", "-p");
        var disabledHookOption = new Option<string[]>("--disable-hook");
        disabledHookOption.CompletionSources.Add(_hooks);
        var replaceOption = new Option<bool>("--replace");
        var command = new Command(name, "Add or replace a composite pack reference.")
        {
            idArgument,
            versionArgument,
            parameterOption,
            disabledHookOption,
            replaceOption,
        };
        command.SetAction(async parseResult =>
            await SetReferenceAsync(
                ResolveWorkspace(parseResult, projectDirectory, workspaceOption),
                parseResult.GetValue(idArgument),
                parseResult.GetValue(versionArgument),
                parseResult.GetValue(parameterOption) ?? [],
                parseResult.GetValue(disabledHookOption) ?? [],
                replaceByDefault || parseResult.GetValue(replaceOption)
            )
        );
        return command;
    }

    private async Task<int> SetReferenceAsync(
        string workspace,
        string? id,
        string? version,
        string[] rawBindings,
        string[] hooks,
        bool replace
    )
    {
        var bindings = ParseBindings(rawBindings);
        if (
            string.IsNullOrEmpty(id)
            || version is null
            || !ManifestModelValidator.IsSemanticVersion(version)
            || bindings.Value is not { } parameters
            || hooks.Any(hook => !IsHook(hook))
        )
        {
            return console.Fail(
                bindings.Error ?? "Reference requires an ID, exact version, and valid hooks."
            );
        }

        var result = await manifestStore.UpdateAsync(
            workspace,
            manifest =>
            {
                var existing = manifest.Packs.FindIndex(reference =>
                    string.Equals(reference.Id, id, StringComparison.Ordinal)
                );
                if (existing >= 0 && !replace)
                {
                    return $"Pack reference '{id}' already exists; use '--replace'.";
                }

                var reference = new PackManifest.PackReference
                {
                    Id = id,
                    Version = version,
                    Parameters = parameters,
                    DisabledHooks = [.. hooks],
                };
                if (existing >= 0)
                {
                    manifest.Packs[existing] = reference;
                }
                else
                {
                    manifest.Packs.Add(reference);
                }

                return null;
            }
        );
        return ReportMutation(result, $"Set pack reference '{id}'.");
    }

    private Command CreateParameterCommand(string projectDirectory, Option<string?> workspaceOption)
    {
        var nameArgument = new Argument<string>("name");
        var typeArgument = new Argument<string>("type");
        typeArgument.CompletionSources.Add("bool", "enum", "string");
        var valueOption = new Option<string[]>("--value", "-v");
        var requiredOption = new Option<bool>("--required");
        var defaultOption = new Option<string[]>("--default");
        var multipleOption = new Option<bool>("--multiple");
        var displayNameOption = new Option<string?>("--display-name");
        var descriptionOption = new Option<string?>("--description", "-d");
        var command = new Command("parameter", "Set a pack parameter.")
        {
            nameArgument,
            typeArgument,
            valueOption,
            requiredOption,
            defaultOption,
            multipleOption,
            displayNameOption,
            descriptionOption,
        };
        command.SetAction(async parseResult =>
        {
            var name = parseResult.GetValue(nameArgument);
            var type = parseResult.GetValue(typeArgument);
            if (string.IsNullOrEmpty(name) || type is not ("string" or "bool" or "enum"))
            {
                return console.Fail("Parameter requires a name and type: string, bool, or enum.");
            }

            var values = parseResult.GetValue(valueOption) ?? [];
            var multiple = parseResult.GetValue(multipleOption);
            var defaultValue = ParseParameterDefault(
                type,
                parseResult.GetValue(defaultOption) ?? [],
                multiple
            );
            if (!defaultValue.IsSuccess)
            {
                return console.Fail(defaultValue.Error);
            }
            var result = await manifestStore.UpdateAsync(
                ResolveWorkspace(parseResult, projectDirectory, workspaceOption),
                manifest =>
                {
                    manifest.Parameters[name] = new PackManifest.PackParameter
                    {
                        Type = type,
                        Required = parseResult.GetValue(requiredOption),
                        Default = defaultValue.Value,
                        Multiple = multiple ? true : null,
                        Values = string.Equals(type, "enum", StringComparison.Ordinal)
                            ? [.. values]
                            : null,
                        DisplayName = parseResult.GetValue(displayNameOption),
                        Description = parseResult.GetValue(descriptionOption),
                    };
                    return null;
                }
            );
            return ReportMutation(result, $"Set parameter '{name}'.");
        });
        return command;
    }

    private static ManifestOperationResult<object?> ParseParameterDefault(
        string type,
        string[] values,
        bool multiple
    )
    {
        if (multiple)
        {
            return string.Equals(type, "enum", StringComparison.Ordinal)
                ? ManifestOperationResult<object?>.Success(values.ToList())
                : ManifestOperationResult<object?>.Failure(
                    "Only enum parameters can be multi-select."
                );
        }

        if (values.Length > 1)
        {
            return ManifestOperationResult<object?>.Failure(
                "Scalar parameters accept at most one default value."
            );
        }

        if (values.Length == 0)
        {
            return ManifestOperationResult<object?>.Success(null);
        }

        var value = values[0];
        return string.Equals(type, "bool", StringComparison.Ordinal)
            ? bool.TryParse(value, out var booleanValue)
                ? ManifestOperationResult<object?>.Success(booleanValue)
                : ManifestOperationResult<object?>.Failure(
                    "Boolean parameter default must be 'true' or 'false'."
                )
            : ManifestOperationResult<object?>.Success(value);
    }

    private Command CreateTagCommand(
        string name,
        string projectDirectory,
        Option<string?> workspaceOption,
        bool remove
    )
    {
        var valueArgument = new Argument<string>("value");
        var command = new Command(name, remove ? "Remove a pack tag." : "Add a pack tag.")
        {
            valueArgument,
        };
        command.SetAction(async parseResult =>
        {
            var value = parseResult.GetValue(valueArgument);
            if (string.IsNullOrEmpty(value))
            {
                return console.Fail("A non-empty tag is required.");
            }

            var result = await manifestStore.UpdateAsync(
                ResolveWorkspace(parseResult, projectDirectory, workspaceOption),
                manifest =>
                {
                    if (remove)
                    {
                        return manifest.Tags.Remove(value) ? null : $"Tag '{value}' was not found.";
                    }

                    if (manifest.Tags.Contains(value, StringComparer.Ordinal))
                    {
                        return $"Tag '{value}' already exists.";
                    }

                    manifest.Tags.Add(value);
                    manifest.Tags.Sort(StringComparer.Ordinal);
                    return null;
                }
            );
            return ReportMutation(result, $"{(remove ? "Removed" : "Added")} tag '{value}'.");
        });
        return command;
    }

    private Command CreateRemoveCommand(string projectDirectory, Option<string?> workspaceOption)
    {
        var selectorArgument = new Argument<string?>("selector")
        {
            Arity = ArgumentArity.ZeroOrOne,
        };
        var command = new Command("rm", "Remove pack content.") { selectorArgument };
        command.Aliases.Add("remove");
        command.SetAction(async parseResult =>
        {
            var selector = parseResult.GetValue(selectorArgument);
            if (string.IsNullOrEmpty(selector))
            {
                return console.Fail("A managed selector is required.");
            }

            var normalizedResult = NormalizeSelector(
                ResolveWorkspace(parseResult, projectDirectory, workspaceOption),
                "glob",
                selector
            );
            if (normalizedResult.Value is not { } normalized)
            {
                return console.Fail(normalizedResult.Error);
            }

            var result = await manifestStore.UpdateAsync(
                ResolveWorkspace(parseResult, projectDirectory, workspaceOption),
                manifest =>
                {
                    var matches = manifest
                        .ManagedFiles.Select((file, index) => (File: file, Index: index))
                        .Where(item =>
                            string.Equals(
                                GetSelector(item.File),
                                normalized,
                                StringComparison.Ordinal
                            )
                        )
                        .ToArray();
                    if (matches.Length != 1)
                    {
                        return $"Managed selector '{normalized}' must match exactly one entry.";
                    }

                    manifest.ManagedFiles.RemoveAt(matches[0].Index);
                    return null;
                }
            );
            return ReportMutation(result, $"Removed managed selector '{normalized}'.");
        });
        command.Add(CreateRemovePackSourceCommand(projectDirectory, workspaceOption));
        command.Add(CreateRemoveHookCommand(projectDirectory, workspaceOption));
        command.Add(CreateRemoveNamedCommand("reference", projectDirectory, workspaceOption));
        command.Add(CreateRemoveNamedCommand("parameter", projectDirectory, workspaceOption));
        command.Add(CreateRemoveNamedCommand("metadata", projectDirectory, workspaceOption));
        command.Add(CreateTagCommand("tag", projectDirectory, workspaceOption, true));
        return command;
    }

    private Command CreateRemoveHookCommand(
        string projectDirectory,
        Option<string?> workspaceOption
    )
    {
        var hookArgument = new Argument<string>("event");
        hookArgument.CompletionSources.Add(_hooks);
        var positionArgument = new Argument<int>("position");
        var command = new Command("hook", "Remove a positioned lifecycle hook.")
        {
            hookArgument,
            positionArgument,
        };
        command.SetAction(async parseResult =>
        {
            var hook = parseResult.GetValue(hookArgument);
            var position = parseResult.GetValue(positionArgument);
            if (hook is null || !IsHook(hook))
            {
                return console.Fail("A supported lifecycle hook event is required.");
            }

            var result = await manifestStore.UpdateAsync(
                ResolveWorkspace(parseResult, projectDirectory, workspaceOption),
                manifest => RemoveHook(manifest, hook, position)
            );
            return ReportMutation(result, $"Removed lifecycle hook '{hook}' position {position}.");
        });
        return command;
    }

    private Command CreateRemoveNamedCommand(
        string kind,
        string projectDirectory,
        Option<string?> workspaceOption
    )
    {
        var nameArgument = new Argument<string>("name");
        var command = new Command(kind, $"Remove pack {kind}.") { nameArgument };
        command.SetAction(async parseResult =>
        {
            var name = parseResult.GetValue(nameArgument);
            if (string.IsNullOrEmpty(name))
            {
                return console.Fail($"A {kind} name is required.");
            }

            var result = await manifestStore.UpdateAsync(
                ResolveWorkspace(parseResult, projectDirectory, workspaceOption),
                manifest => RemoveNamed(manifest, kind, name)
            );
            return ReportMutation(result, $"Removed {kind} '{name}'.");
        });
        return command;
    }

    private Command CreateDisplayCommand(
        string name,
        string projectDirectory,
        Option<string?> workspaceOption
    )
    {
        var command = new Command(name, $"Display local pack {name}.");
        command.SetAction(async parseResult =>
        {
            var result = await manifestStore.LoadAsync(
                ResolveWorkspace(parseResult, projectDirectory, workspaceOption)
            );
            if (result.Value is not { } manifest)
            {
                return console.Fail(result.Error);
            }

            var renderables = name switch
            {
                "list" => PackAuthoringFormatter.FormatList(manifest),
                "hooks" => PackAuthoringFormatter.FormatHooks(manifest),
                "show" => PackAuthoringFormatter.FormatSummary(manifest),
                "sources" => PackAuthoringFormatter.FormatSources(manifest),
                _ => throw new InvalidOperationException(
                    $"Unsupported pack display command '{name}'."
                ),
            };
            foreach (var renderable in renderables)
            {
                console.Render(renderable);
            }

            RenderNextSteps(NextStepContext.PackDisplayed);
            return 0;
        });
        return command;
    }

    private Command CreateValidateCommand(string projectDirectory, Option<string?> workspaceOption)
    {
        var command = new Command("validate", "Validate the local pack manifest.");
        command.SetAction(async parseResult =>
        {
            var workspace = ResolveWorkspace(parseResult, projectDirectory, workspaceOption);
            var result = await manifestStore.LoadAsync(workspace);
            if (result.Value is not { } manifest)
            {
                return console.Fail(result.Error);
            }

            var sourceFiles = fileSystem
                .Directory.EnumerateFiles(workspace, "*", SearchOption.AllDirectories)
                .Select(path => fileSystem.Path.GetRelativePath(workspace, path))
                .ToArray();
            var issues = await PackManifestValidator.ValidateAsync(manifest, sourceFiles);
            if (issues.Count > 0)
            {
                foreach (var issue in issues)
                {
                    console.Error(issue);
                }

                return 1;
            }

            var externalValidation = await validationService.ValidateExternalSourcesAsync(
                workspace,
                manifest
            );
            if (!externalValidation.IsSuccess)
            {
                return console.Fail(externalValidation.Error);
            }

            var usedAliases = manifest
                .ManagedFiles.Select(file =>
                    PackManagedFileSelector.Create(file).Value?.SourceAlias
                )
                .OfType<string>()
                .ToHashSet(StringComparer.Ordinal);
            foreach (var alias in manifest.Sources.Keys.Except(usedAliases, StringComparer.Ordinal))
            {
                console.Warning($"Source alias '{alias}' is unused.");
            }

            console.Info("Manifest valid.");
            RenderNextSteps(NextStepContext.PackValidated);
            return 0;
        });
        return command;
    }

    private string ResolveWorkspace(
        ParseResult parseResult,
        string projectDirectory,
        Option<string?> workspaceOption
    ) =>
        workspaceDirectoryResolver.Resolve(projectDirectory, parseResult.GetValue(workspaceOption));

    private ManifestOperationResult<string> NormalizeSelector(
        string workspace,
        string kind,
        string value
    )
    {
        if (!string.Equals(kind, "glob", StringComparison.Ordinal))
        {
            return ProjectPath.NormalizeProjectRelativePath(fileSystem, workspace, value);
        }

        var normalized = ProjectPath.Normalize(value);
        var isInvalidGlob =
            normalized.Length == 0
            || normalized.StartsWith('/')
            || (normalized.Length >= 2 && char.IsAsciiLetter(normalized[0]) && normalized[1] == ':')
            || normalized.Split('/').Contains("..", StringComparer.Ordinal);
        if (isInvalidGlob)
        {
            return ManifestOperationResult<string>.Failure(
                "Glob must be a non-empty pattern relative to the pack directory."
            );
        }

        return ManifestOperationResult<string>.Success(normalized);
    }

    private static string? DeriveGlobTarget(string glob)
    {
        var segments = glob.Split('/');
        var fixedSegments = segments.TakeWhile(segment =>
            !segment.Contains('*') && !segment.Contains('?') && !segment.Contains('[')
        );
        var target = string.Join('/', fixedSegments);
        return target.Length == 0 ? null : target;
    }

    private static ManifestOperationResult<PackManifest.PackManagedFileStrategy> ParseStrategy(
        string? value
    )
    {
        if (value is null)
        {
            return ManifestOperationResult<PackManifest.PackManagedFileStrategy>.Success(
                PackManifest.PackManagedFileStrategy.CopyOverwrite
            );
        }

        var parts = value.Split(':', 2);
        if (parts.Length != 2)
        {
            return ManifestOperationResult<PackManifest.PackManagedFileStrategy>.Failure(
                "Strategy must use <type>:<method>."
            );
        }

        return ManifestOperationResult<PackManifest.PackManagedFileStrategy>.Success(
            new PackManifest.PackManagedFileStrategy { Type = parts[0], Method = parts[1] }
        );
    }

    private static ManifestOperationResult<Dictionary<string, object>> ParseBindings(
        IEnumerable<string> values
    )
    {
        var result = new Dictionary<string, object>(StringComparer.Ordinal);
        foreach (var value in values)
        {
            var separator = value.IndexOf('=');
            if (separator <= 0)
            {
                return ManifestOperationResult<Dictionary<string, object>>.Failure(
                    "Parameter bindings must use <name>=<value>."
                );
            }

            var name = value[..separator];
            var raw = value[(separator + 1)..];
            result[name] = bool.TryParse(raw, out var boolean) ? boolean : raw;
        }

        return ManifestOperationResult<Dictionary<string, object>>.Success(result);
    }

    private static string? SetMetadata(PackManifest manifest, string property, string value)
    {
        switch (property)
        {
            case "id":
                manifest.Id = value;
                break;
            case "name":
                manifest.Name = value;
                break;
            case "version":
                manifest.Version = value;
                break;
            case "description":
                manifest.Description = value;
                break;
            case "author":
                manifest.Author = value;
                break;
            case "homepage":
                manifest.Homepage = value;
                break;
            case "license":
                manifest.License = value;
                break;
            default:
                return $"Unsupported pack property '{property}'.";
        }

        return null;
    }

    private static string? RemoveNamed(PackManifest manifest, string kind, string name)
    {
        switch (kind)
        {
            case "reference":
                return
                    manifest.Packs.RemoveAll(reference =>
                        string.Equals(reference.Id, name, StringComparison.Ordinal)
                    ) == 1
                    ? null
                    : $"Pack reference '{name}' was not found.";
            case "parameter":
                return manifest.Parameters.Remove(name)
                    ? null
                    : $"Parameter '{name}' was not found.";
            case "metadata":
                if (name is "id" or "version")
                {
                    return $"Required metadata '{name}' cannot be removed.";
                }

                return SetOptionalMetadata(manifest, name);
            default:
                return $"Unsupported removal type '{kind}'.";
        }
    }

    private static string? SetOptionalMetadata(PackManifest manifest, string name)
    {
        switch (name)
        {
            case "name":
                manifest.Name = null;
                break;
            case "description":
                manifest.Description = null;
                break;
            case "author":
                manifest.Author = null;
                break;
            case "homepage":
                manifest.Homepage = null;
                break;
            case "license":
                manifest.License = null;
                break;
            default:
                return $"Unsupported pack property '{name}'.";
        }

        return null;
    }

    private int ReportMutation(
        ManifestOperationResult<PackManifest> result,
        string successMessage,
        NextStepContext context = NextStepContext.PackModified,
        string? value = null
    )
    {
        if (result.Value is null)
        {
            return console.Fail(result.Error);
        }

        console.Info(successMessage);
        RenderNextSteps(context, value);
        return 0;
    }

    private void RenderNextSteps(NextStepContext context, string? value = null) =>
        nextStepRenderer.Render(nextStepAdvisor.Recommend(context, value), "Next steps:");

    private static bool IsHook(string? hook) => _hooks.Contains(hook, StringComparer.Ordinal);

    private static string? RemoveHook(PackManifest manifest, string hook, int position)
    {
        if (manifest.Hooks is not { } hooks)
        {
            return $"Lifecycle hook position '{position}' does not exist in '{hook}'.";
        }

        var declarations = GetHooks(hooks, hook);
        if (position <= 0 || declarations is null || position > declarations.Count)
        {
            return $"Lifecycle hook position '{position}' does not exist in '{hook}'.";
        }

        declarations.RemoveAt(position - 1);
        if (declarations.Count == 0)
        {
            SetHooks(hooks, hook, null);
        }

        return null;
    }

    private static List<PackManifest.PackHook>? GetHooks(
        PackManifest.PackHooks hooks,
        string hook
    ) =>
        hook switch
        {
            "preInstall" => hooks.PreInstall,
            "postInstall" => hooks.PostInstall,
            "postUninstall" => hooks.PostUninstall,
            "preUpdate" => hooks.PreUpdate,
            "postUpdate" => hooks.PostUpdate,
            "preUninstall" => hooks.PreUninstall,
            _ => null,
        };

    private static void SetHooks(
        PackManifest.PackHooks hooks,
        string hook,
        List<PackManifest.PackHook>? declarations
    )
    {
        switch (hook)
        {
            case "preInstall":
                hooks.PreInstall = declarations;
                break;
            case "postInstall":
                hooks.PostInstall = declarations;
                break;
            case "postUninstall":
                hooks.PostUninstall = declarations;
                break;
            case "preUpdate":
                hooks.PreUpdate = declarations;
                break;
            case "postUpdate":
                hooks.PostUpdate = declarations;
                break;
            case "preUninstall":
                hooks.PreUninstall = declarations;
                break;
        }
    }

    private static string? GetSelector(PackManifest.PackManagedFile file) =>
        PackManagedFileSelector.Create(file).Value?.Value
        ?? file.Path
        ?? file.Source
        ?? file.Directory
        ?? file.Glob;

    private static string? GetSelectorSourceAlias(PackManifest.PackManagedFile file) =>
        PackManagedFileSelector.Create(file).Value?.SourceAlias;

    private static void SetSelector(
        PackManifest.PackManagedFile file,
        string kind,
        string selector,
        string? sourceAlias
    )
    {
        file.Source = sourceAlias;
        switch (kind)
        {
            case "file":
                file.Path = selector;
                break;
            case "directory":
                file.Directory = selector;
                break;
            case "glob":
                file.Glob = selector;
                break;
        }
    }

    private static ManifestOperationResult<string> NormalizeExternalSelector(
        string kind,
        string value
    )
    {
        var normalized = ProjectPath.Normalize(value);
        var isInvalidExternalSelector =
            normalized.Length == 0
            || normalized.StartsWith('/')
            || normalized.Split('/').Contains("..", StringComparer.Ordinal)
            || (
                normalized.Length >= 2 && char.IsAsciiLetter(normalized[0]) && normalized[1] == ':'
            );
        if (isInvalidExternalSelector)
        {
            return ManifestOperationResult<string>.Failure(
                $"A {kind} selector must be a non-empty path relative to the external source root."
            );
        }

        return ManifestOperationResult<string>.Success(normalized);
    }

    private Command CreatePackSourceCommand(
        string projectDirectory,
        Option<string?> workspaceOption
    ) =>
        new("source", "Add a pack-defined Git source alias.")
        {
            CreatePackSourceVariantCommand("git", projectDirectory, workspaceOption),
            CreatePackSourceVariantCommand("github", projectDirectory, workspaceOption),
        };

    private Command CreatePackSourceVariantCommand(
        string variant,
        string projectDirectory,
        Option<string?> workspaceOption
    )
    {
        var nameArgument = new Argument<string>("name")
        {
            Description = "Pack-local source alias.",
        };
        var locationArgument = new Argument<string>(
            string.Equals(variant, "github", StringComparison.Ordinal)
                ? "owner/repository"
                : "repository-url"
        )
        {
            Description = string.Equals(variant, "github", StringComparison.Ordinal)
                ? "GitHub repository in owner/repository form."
                : "Git repository URL.",
        };
        var refOption = new Option<string?>("--ref", "-r") { Description = "Branch or tag." };
        var pathOption = new Option<string?>("--path", "-p")
        {
            Description = "Repository-relative base path.",
        };
        var descriptionOption = new Option<string?>("--description", "-d")
        {
            Description = "Source description.",
        };
        var manifestOption = new Option<string?>("--manifest")
        {
            Description = "Directory that contains the pack manifest.",
        };
        var command = new Command(variant, $"Add a pack-defined {variant} source.")
        {
            nameArgument,
            locationArgument,
            refOption,
            pathOption,
            descriptionOption,
            manifestOption,
        };
        command.SetAction(async parseResult =>
            await AddPackSourceAsync(
                ResolveManifestDirectory(
                    parseResult,
                    projectDirectory,
                    workspaceOption,
                    manifestOption
                ),
                variant,
                parseResult.GetValue(nameArgument),
                parseResult.GetValue(locationArgument),
                parseResult.GetValue(refOption),
                parseResult.GetValue(pathOption),
                parseResult.GetValue(descriptionOption)
            )
        );
        return command;
    }

    private string ResolveManifestDirectory(
        ParseResult parseResult,
        string projectDirectory,
        Option<string?> workspaceOption,
        Option<string?> manifestOption
    )
    {
        var workspace = ResolveWorkspace(parseResult, projectDirectory, workspaceOption);
        var manifestDirectory = parseResult.GetValue(manifestOption);
        return string.IsNullOrWhiteSpace(manifestDirectory)
            ? workspace
            : fileSystem.Path.GetFullPath(manifestDirectory, workspace);
    }

    private async Task<int> AddPackSourceAsync(
        string manifestDirectory,
        string variant,
        string? alias,
        string? location,
        string? gitRef,
        string? basePath,
        string? description
    )
    {
        if (string.IsNullOrWhiteSpace(alias))
        {
            return console.Fail("A pack source alias is required.");
        }

        if (string.IsNullOrWhiteSpace(location))
        {
            return console.Fail("A repository location is required.");
        }

        if (string.IsNullOrWhiteSpace(gitRef))
        {
            return console.Fail(
                $"A ref is required. Run 'luna pack add source {variant} {alias} {location} --ref <ref>'."
            );
        }

        var repositoryUrl = location;
        var isInvalidGitHubRepository =
            string.Equals(variant, "github", StringComparison.Ordinal)
            && !GitHubShorthand.TryCreateUrl(location, out repositoryUrl);
        if (isInvalidGitHubRepository)
        {
            return console.Fail("A GitHub repository must use the organization/repository format.");
        }

        var normalizedPath = SourceIdentityNormalizer.NormalizeBasePath(basePath);
        if (!normalizedPath.IsSuccess)
        {
            return console.Fail(normalizedPath.Error);
        }

        var fingerprint = SourceIdentityNormalizer.CreateGit(repositoryUrl, gitRef, basePath);
        if (!fingerprint.IsSuccess)
        {
            return console.Fail(fingerprint.Error);
        }

        var canonicalRef = await CanonicalizePackSourceRefAsync(repositoryUrl, gitRef);
        if (canonicalRef.Value is not { } resolvedRef)
        {
            return console.Fail(canonicalRef.Error);
        }

        var packPath = ProjectPath.NormalizeOptional(basePath)?.Trim('/');
        var result = await manifestStore.UpdateAsync(
            manifestDirectory,
            manifest =>
                AddPackSource(manifest, alias, repositoryUrl, resolvedRef, packPath, description)
        );
        return ReportMutation(
            result,
            $"Added pack source '{alias}'.",
            NextStepContext.PackSourceAdded,
            alias
        );
    }

    private static string? AddPackSource(
        PackManifest manifest,
        string alias,
        string repositoryUrl,
        string resolvedRef,
        string? packPath,
        string? description
    )
    {
        if (manifest.Sources.ContainsKey(alias))
        {
            return $"Pack source alias '{alias}' already exists.";
        }

        manifest.Sources[alias] = new PackManifest.PackSource
        {
            Type = "git",
            Url = repositoryUrl.Trim(),
            Ref = resolvedRef,
            Path = string.IsNullOrEmpty(packPath) ? null : packPath,
            Description = description,
        };
        return null;
    }

    private async Task<ManifestOperationResult<string>> CanonicalizePackSourceRefAsync(
        string repositoryUrl,
        string gitRef
    )
    {
        if (gitRefResolver is null)
        {
            return ManifestOperationResult<string>.Success(gitRef.Trim());
        }

        var canonicalRef = await gitRefResolver.ResolveCanonicalRefAsync(
            repositoryUrl,
            gitRef,
            timeout: null,
            CancellationToken.None
        );
        return canonicalRef.Value is { } resolved
            ? ManifestOperationResult<string>.Success(resolved.CanonicalRef)
            : ManifestOperationResult<string>.Failure(
                canonicalRef.Error ?? $"Unable to canonicalize Git ref '{gitRef}'."
            );
    }

    private Command CreateRemovePackSourceCommand(
        string projectDirectory,
        Option<string?> workspaceOption
    )
    {
        var nameArgument = new Argument<string>("name")
        {
            Description = "Pack-local source alias.",
        };
        var command = new Command("source", "Remove a pack-defined Git source alias.")
        {
            nameArgument,
        };
        command.SetAction(async parseResult =>
        {
            var alias = parseResult.GetValue(nameArgument);
            if (string.IsNullOrWhiteSpace(alias))
            {
                return console.Fail("A pack source alias is required.");
            }

            var result = await manifestStore.UpdateAsync(
                ResolveWorkspace(parseResult, projectDirectory, workspaceOption),
                manifest => RemovePackSource(manifest, alias)
            );
            return ReportMutation(result, $"Removed pack source '{alias}'.");
        });
        return command;
    }

    private static string? RemovePackSource(PackManifest manifest, string alias)
    {
        if (!manifest.Sources.ContainsKey(alias))
        {
            return $"Pack source alias '{alias}' is not declared.";
        }

        var references = manifest.ManagedFiles.Count(file =>
            string.Equals(GetSelectorSourceAlias(file), alias, StringComparison.Ordinal)
        );
        if (references > 0)
        {
            return $"Pack source alias '{alias}' is referenced by {references} managed file(s).";
        }

        manifest.Sources.Remove(alias);
        return null;
    }
}
