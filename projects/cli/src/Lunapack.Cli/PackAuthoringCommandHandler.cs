using System.CommandLine;
using System.IO.Abstractions;

namespace Lunapack.Cli;

internal sealed class PackAuthoringCommandHandler(
    IFileSystem fileSystem,
    PackManifestStore manifestStore,
    WorkspaceDirectoryResolver workspaceDirectoryResolver,
    INextStepAdvisor nextStepAdvisor,
    NextStepRenderer nextStepRenderer,
    CliConsole console
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

    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Design",
        "MA0051:Method is too long",
        Justification = "Command action methods are inherently long."
    )]
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
        var command = new Command(selectorKind, $"Add a managed {selectorKind}.")
        {
            pathArgument,
            targetOption,
            strategyOption,
            templateOption,
            conditionOption,
        };
        command.SetAction(async parseResult =>
            await AddManagedFileAsync(
                ResolveWorkspace(parseResult, projectDirectory, workspaceOption),
                selectorKind,
                parseResult.GetValue(pathArgument),
                parseResult.GetValue(targetOption),
                parseResult.GetValue(strategyOption),
                parseResult.GetValue(templateOption),
                parseResult.GetValue(conditionOption)
            )
        );
        return command;
    }

    private async Task<int> AddManagedFileAsync(
        string workspace,
        string selectorKind,
        string? rawPath,
        string? rawTarget,
        string? rawStrategy,
        bool template,
        string? condition
    )
    {
        if (rawPath is null)
        {
            return console.Fail($"A {selectorKind} path is required.");
        }

        var selector = NormalizeSelector(workspace, selectorKind, rawPath);
        if (selector.Value is not { } normalizedSelector)
        {
            return console.Fail(selector.Error);
        }

        var targetValue =
            rawTarget
            ?? (
                string.Equals(selectorKind, "glob", StringComparison.Ordinal)
                    ? DeriveGlobTarget(normalizedSelector)
                    : normalizedSelector
            );
        if (targetValue is null)
        {
            return console.Fail("Glob target cannot be inferred; provide '--target'.");
        }

        var target = ProjectPath.NormalizeProjectRelativePath(fileSystem, workspace, targetValue);
        var strategy = ParseStrategy(rawStrategy);
        if (target.Value is not { } normalizedTarget || strategy.Value is not { } parsedStrategy)
        {
            return console.Fail(target.Error ?? strategy.Error);
        }

        var result = await manifestStore.UpdateAsync(
            workspace,
            manifest =>
                AddManagedSelector(
                    manifest,
                    selectorKind,
                    normalizedSelector,
                    normalizedTarget,
                    parsedStrategy,
                    template,
                    condition
                )
        );
        return ReportMutation(result, $"Added {selectorKind} '{normalizedSelector}'.");
    }

    private static string? AddManagedSelector(
        PackManifest manifest,
        string selectorKind,
        string selector,
        string target,
        PackManifest.PackManagedFileStrategy strategy,
        bool template,
        string? condition
    )
    {
        if (
            manifest.ManagedFiles.Any(file =>
                string.Equals(GetSelector(file), selector, StringComparison.Ordinal)
            )
        )
        {
            return $"Managed selector '{selector}' already exists.";
        }

        var managedFile = new PackManifest.PackManagedFile
        {
            Target = target,
            Strategy = strategy,
            Template = template,
            Condition = condition,
        };
        SetSelector(managedFile, selectorKind, selector);
        manifest.ManagedFiles.Add(managedFile);
        return null;
    }

    private Command CreateHookCommand(string projectDirectory, Option<string?> workspaceOption) =>
        new Command("hook", "Add a lifecycle hook.")
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
        var executableArgument = new Argument<string>("command");
        var argumentsArgument = new Argument<string[]>("arguments")
        {
            Arity = ArgumentArity.ZeroOrMore,
        };
        var descriptionOption = new Option<string?>("--description", "-d");
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
        var fileArgument = new Argument<string>("file");
        var runnerArgument = new Argument<string>("runner");
        var argumentsArgument = new Argument<string[]>("arguments")
        {
            Arity = ArgumentArity.ZeroOrMore,
        };
        var descriptionOption = new Option<string?>("--description", "-d");
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
        var fileArgument = new Argument<string>("file");
        var templatingOption = new Option<bool>("--templating")
        {
            Description = "Render instruction content with Scriban.",
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
                    Version = version!,
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
        var valueOption = new Option<string[]>("--value", "-v");
        var requiredOption = new Option<bool>("--required");
        var defaultOption = new Option<string?>("--default");
        var displayNameOption = new Option<string?>("--display-name");
        var descriptionOption = new Option<string?>("--description", "-d");
        var command = new Command("parameter", "Set a pack parameter.")
        {
            nameArgument,
            typeArgument,
            valueOption,
            requiredOption,
            defaultOption,
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
            var defaultValue = ParseParameterDefault(type, parseResult.GetValue(defaultOption));
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
        string? value
    ) =>
        value is null ? ManifestOperationResult<object?>.Success(null)
        : string.Equals(type, "bool", StringComparison.Ordinal)
            ? bool.TryParse(value, out var booleanValue)
                    ? ManifestOperationResult<object?>.Success(booleanValue)
                : ManifestOperationResult<object?>.Failure(
                    "Boolean parameter default must be 'true' or 'false'."
                )
        : ManifestOperationResult<object?>.Success(value);

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
            var result = await manifestStore.LoadAsync(
                ResolveWorkspace(parseResult, projectDirectory, workspaceOption)
            );
            if (result.Value is null)
            {
                return console.Fail(result.Error);
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
        if (
            normalized.Length == 0
            || normalized.StartsWith('/')
            || (normalized.Length >= 2 && char.IsAsciiLetter(normalized[0]) && normalized[1] == ':')
            || normalized.Split('/').Contains("..", StringComparer.Ordinal)
        )
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

    private int ReportMutation(ManifestOperationResult<PackManifest> result, string successMessage)
    {
        if (result.Value is null)
        {
            return console.Fail(result.Error);
        }

        console.Info(successMessage);
        RenderNextSteps(NextStepContext.PackModified);
        return 0;
    }

    private void RenderNextSteps(NextStepContext context) =>
        nextStepRenderer.Render(nextStepAdvisor.Recommend(context));

    private static bool IsHook(string? hook) => _hooks.Contains(hook, StringComparer.Ordinal);

    private static string? RemoveHook(PackManifest manifest, string hook, int position)
    {
        var declarations = manifest.Hooks is null ? null : GetHooks(manifest.Hooks, hook);
        if (position <= 0 || declarations is null || position > declarations.Count)
        {
            return $"Lifecycle hook position '{position}' does not exist in '{hook}'.";
        }

        declarations.RemoveAt(position - 1);
        if (declarations.Count == 0)
        {
            SetHooks(manifest.Hooks!, hook, null);
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
        file.Source ?? file.Directory ?? file.Glob;

    private static void SetSelector(PackManifest.PackManagedFile file, string kind, string selector)
    {
        switch (kind)
        {
            case "file":
                file.Source = selector;
                break;
            case "directory":
                file.Directory = selector;
                break;
            case "glob":
                file.Glob = selector;
                break;
        }
    }
}
