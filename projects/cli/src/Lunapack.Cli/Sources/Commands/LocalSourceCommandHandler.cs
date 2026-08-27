using System.CommandLine;
using System.IO.Abstractions;

namespace Lunapack.Cli;

internal sealed class LocalSourceCommandHandler(
    IFileSystem fileSystem,
    IProjectStateStore projectStateStore,
    WorkspaceDirectoryResolver workspaceDirectoryResolver,
    INextStepAdvisor nextStepAdvisor,
    NextStepRenderer nextStepRenderer,
    CliConsole console,
    GitRefResolver? gitRefResolver = null
)
{
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Maintainability",
        "MA0051:Method is too long",
        Justification = "CLI option definitions remain collocated with their command actions."
    )]
    public Command CreateCommand(string projectDirectory, Option<string?> workspaceOption)
    {
        var sourceNameArgument = new Argument<string>("name")
        {
            Description = "Unique name for the pack source.",
        };
        var sourcePathArgument = new Argument<string>("path")
        {
            Description = "Path to a local pack source.",
        };
        var addLocalSourceCommand = new Command("local", "Add a local pack source.")
        {
            sourceNameArgument,
            sourcePathArgument,
        };
        addLocalSourceCommand.SetAction(async parseResult =>
        {
            var sourceName = parseResult.GetValue(sourceNameArgument);
            var sourcePath = parseResult.GetValue(sourcePathArgument);
            return sourceName is null || sourcePath is null
                ? console.Fail("A source name and local source path are required.")
                : await AddLocalSourceAsync(
                    workspaceDirectoryResolver.Resolve(
                        projectDirectory,
                        parseResult.GetValue(workspaceOption)
                    ),
                    sourceName,
                    sourcePath
                );
        });

        var repositoryUrlArgument = new Argument<string>("repository-url")
        {
            Description = "URL of a Git repository containing pack manifests.",
        };
        var gitRefOption = new Option<string?>("--ref", "-r")
        {
            Description = "Branch or commit SHA to resolve.",
        };
        var gitPathOption = new Option<string?>("--path", "-p")
        {
            Description = "Repository-relative path to search for packs.",
        };
        var addGitSourceCommand = new Command("git", "Add a Git pack source.")
        {
            sourceNameArgument,
            repositoryUrlArgument,
            gitRefOption,
            gitPathOption,
        };
        addGitSourceCommand.SetAction(async parseResult =>
        {
            var sourceName = parseResult.GetValue(sourceNameArgument);
            var repositoryUrl = parseResult.GetValue(repositoryUrlArgument);
            return sourceName is null || repositoryUrl is null
                ? console.Fail("A source name and Git repository URL are required.")
                : await AddGitSourceAsync(
                    workspaceDirectoryResolver.Resolve(
                        projectDirectory,
                        parseResult.GetValue(workspaceOption)
                    ),
                    sourceName,
                    repositoryUrl,
                    parseResult.GetValue(gitRefOption),
                    parseResult.GetValue(gitPathOption)
                );
        });

        var githubRepositoryArgument = new Argument<string>("organization/repository")
        {
            Description = "GitHub repository coordinate containing pack manifests.",
        };
        var addGitHubSourceCommand = new Command("github", "Add a GitHub pack source.")
        {
            sourceNameArgument,
            githubRepositoryArgument,
            gitRefOption,
            gitPathOption,
        };
        addGitHubSourceCommand.SetAction(async parseResult =>
        {
            var sourceName = parseResult.GetValue(sourceNameArgument);
            var repository = parseResult.GetValue(githubRepositoryArgument);
            if (
                sourceName is null
                || repository is null
                || !GitHubShorthand.TryCreateUrl(repository, out var repositoryUrl)
            )
            {
                return console.Fail(
                    "A GitHub repository must use the organization/repository format."
                );
            }

            if (string.IsNullOrWhiteSpace(parseResult.GetValue(gitRefOption)))
            {
                return console.Fail("GitHub sources require an explicit --ref.");
            }

            return await AddGitSourceAsync(
                workspaceDirectoryResolver.Resolve(
                    projectDirectory,
                    parseResult.GetValue(workspaceOption)
                ),
                sourceName,
                repositoryUrl,
                parseResult.GetValue(gitRefOption),
                parseResult.GetValue(gitPathOption)
            );
        });

        var addSourceCommand = new Command("add", "Add a pack source.")
        {
            addLocalSourceCommand,
            addGitSourceCommand,
            addGitHubSourceCommand,
        };
        var listSourcesCommand = new Command("list", "List configured pack sources.");
        listSourcesCommand.SetAction(async parseResult =>
            await ListAsync(
                workspaceDirectoryResolver.Resolve(
                    projectDirectory,
                    parseResult.GetValue(workspaceOption)
                )
            )
        );
        var removeSourceNameArgument = new Argument<string>("name")
        {
            Description = "Name of the configured source to remove.",
        };
        var removeSourceCommand = new Command("rm", "Remove a configured pack source.")
        {
            removeSourceNameArgument,
        };
        removeSourceCommand.Aliases.Add("remove");
        removeSourceCommand.SetAction(async parseResult =>
        {
            var sourceName = parseResult.GetValue(removeSourceNameArgument);
            return sourceName is null
                ? console.Fail("A source name is required.")
                : await RemoveAsync(
                    workspaceDirectoryResolver.Resolve(
                        projectDirectory,
                        parseResult.GetValue(workspaceOption)
                    ),
                    sourceName
                );
        });

        var currentSourceNameArgument = new Argument<string>("current-id")
        {
            Description = "Name of the configured source to rename.",
        };
        var newSourceNameArgument = new Argument<string>("new-id")
        {
            Description = "New unique name for the configured source.",
        };
        var renameSourceCommand = new Command("rename", "Rename a configured pack source.")
        {
            currentSourceNameArgument,
            newSourceNameArgument,
        };
        renameSourceCommand.SetAction(async parseResult =>
        {
            var currentName = parseResult.GetValue(currentSourceNameArgument);
            var newName = parseResult.GetValue(newSourceNameArgument);
            return currentName is null || newName is null
                ? console.Fail("A current and new source name are required.")
                : await RenameAsync(
                    workspaceDirectoryResolver.Resolve(
                        projectDirectory,
                        parseResult.GetValue(workspaceOption)
                    ),
                    currentName,
                    newName
                );
        });

        return new Command("sources", "Manage pack sources.")
        {
            addSourceCommand,
            listSourcesCommand,
            removeSourceCommand,
            renameSourceCommand,
        };
    }

    public async Task<int> ListAsync(string projectDirectory)
    {
        var loadedState = await projectStateStore.LoadAsync(projectDirectory);
        if (loadedState.Value is not { } state)
        {
            return console.Fail(loadedState.Error);
        }

        foreach (var source in state.Configuration.Sources)
        {
            console.Info(SourceOutputFormatter.Format(source));
        }

        return 0;
    }

    public async Task<int> AddLocalSourceAsync(string projectDirectory, string name, string path)
    {
        if (string.IsNullOrEmpty(name))
        {
            return console.Fail("A source name is required.");
        }

        if (fileSystem.Path.IsPathRooted(path))
        {
            return console.Fail("Local source paths must be relative to the project directory.");
        }

        var sourcePath = fileSystem.Path.GetFullPath(path, projectDirectory);
        if (!fileSystem.Directory.Exists(sourcePath))
        {
            return console.Fail($"Local source directory '{path}' does not exist.");
        }

        var normalizedPath = ProjectPath.Normalize(
            fileSystem.Path.GetRelativePath(projectDirectory, sourcePath)
        );
        var loadedState = await projectStateStore.LoadAsync(projectDirectory);
        if (loadedState.Value is not { } state)
        {
            return console.Fail(loadedState.Error);
        }

        if (SourceNameExists(state.Configuration, name))
        {
            return console.Fail($"Source name '{name}' is already configured.");
        }

        var candidateLocalSource = new ProjectConfiguration.LocalSource
        {
            Name = name,
            Path = normalizedPath,
        };
        if (FindFingerprintConflict(state.Configuration, candidateLocalSource) is { } conflict)
        {
            return console.Fail(
                $"Local source '{normalizedPath}' is already configured as '{conflict}'."
            );
        }

        state.Configuration.Sources.Add(candidateLocalSource);
        var savedState = await projectStateStore.SaveAllowingUnavailableSourcesAsync(
            projectDirectory,
            state
        );

        return CompleteSourceAddition(savedState, name);
    }

    public async Task<int> AddGitSourceAsync(
        string projectDirectory,
        string name,
        string repositoryUrl,
        string? gitRef,
        string? repositoryPath
    )
    {
        var candidate = await CreateGitSourceAsync(name, repositoryUrl, gitRef, repositoryPath);
        if (candidate.Value is not { } candidateGitSource)
        {
            return console.Fail(candidate.Error);
        }

        var loadedState = await projectStateStore.LoadAsync(projectDirectory);
        if (loadedState.Value is not { } state)
        {
            return console.Fail(loadedState.Error);
        }

        if (SourceNameExists(state.Configuration, name))
        {
            return console.Fail($"Source name '{name}' is already configured.");
        }

        if (FindFingerprintConflict(state.Configuration, candidateGitSource) is { } conflict)
        {
            return console.Fail(
                $"Git source '{repositoryUrl}' is already configured as '{conflict}'."
            );
        }

        state.Configuration.Sources.Add(candidateGitSource);
        var savedState = await projectStateStore.SaveAllowingUnavailableSourcesAsync(
            projectDirectory,
            state
        );

        return CompleteSourceAddition(savedState, name);
    }

    private async Task<
        ManifestOperationResult<ProjectConfiguration.GitSource>
    > CreateGitSourceAsync(
        string name,
        string repositoryUrl,
        string? gitRef,
        string? repositoryPath
    )
    {
        if (string.IsNullOrEmpty(name))
        {
            return ManifestOperationResult<ProjectConfiguration.GitSource>.Failure(
                "A source name is required."
            );
        }

        if (string.IsNullOrWhiteSpace(repositoryUrl))
        {
            return ManifestOperationResult<ProjectConfiguration.GitSource>.Failure(
                "A Git repository URL is required."
            );
        }

        var normalizedRepositoryPath = ProjectPath.NormalizeOptional(repositoryPath)?.Trim('/');
        if (!IsSafeRepositoryPath(normalizedRepositoryPath))
        {
            return ManifestOperationResult<ProjectConfiguration.GitSource>.Failure(
                "Git source paths must be repository-relative and must not contain '..'."
            );
        }

        var repository = SourceIdentityNormalizer.NormalizeRepository(repositoryUrl);
        if (!repository.IsSuccess)
        {
            return ManifestOperationResult<ProjectConfiguration.GitSource>.Failure(
                repository.Error ?? "The Git repository URL is invalid."
            );
        }

        var canonicalRef = await CanonicalizeRefAsync(repositoryUrl, gitRef);
        return canonicalRef.IsSuccess
            ? ManifestOperationResult<ProjectConfiguration.GitSource>.Success(
                new ProjectConfiguration.GitSource
                {
                    Name = name,
                    Url = repositoryUrl.Trim(),
                    Ref = canonicalRef.Value,
                    Path = string.IsNullOrEmpty(normalizedRepositoryPath)
                        ? null
                        : normalizedRepositoryPath,
                }
            )
            : ManifestOperationResult<ProjectConfiguration.GitSource>.Failure(
                canonicalRef.Error ?? "The Git ref is invalid."
            );
    }

    private async Task<ManifestOperationResult<string?>> CanonicalizeRefAsync(
        string repositoryUrl,
        string? gitRef
    )
    {
        if (string.IsNullOrWhiteSpace(gitRef))
        {
            return ManifestOperationResult<string?>.Success(null);
        }

        if (gitRefResolver is null)
        {
            return ManifestOperationResult<string?>.Success(gitRef.Trim());
        }

        var canonicalRef = await gitRefResolver.ResolveCanonicalRefAsync(
            repositoryUrl,
            gitRef,
            timeout: null,
            CancellationToken.None
        );
        return canonicalRef.Value is { } resolved
            ? ManifestOperationResult<string?>.Success(resolved.CanonicalRef)
            : ManifestOperationResult<string?>.Failure(
                canonicalRef.Error ?? $"Unable to canonicalize Git ref '{gitRef}'."
            );
    }

    private static string? FindFingerprintConflict(
        ProjectConfiguration configuration,
        ProjectConfiguration.Source candidate
    )
    {
        var created = SourceIdentityNormalizer.Create(candidate);
        if (created.Value is not { } fingerprint)
        {
            return null;
        }

        foreach (var source in configuration.Sources)
        {
            var existing = SourceIdentityNormalizer.Create(source);
            if (
                existing.Value is { } existingFingerprint
                && string.Equals(
                    existingFingerprint.Value,
                    fingerprint.Value,
                    StringComparison.Ordinal
                )
            )
            {
                return source.Name;
            }
        }

        return null;
    }

    public async Task<int> RemoveAsync(string projectDirectory, string name)
    {
        var loadedState = await projectStateStore.LoadAsync(projectDirectory);
        if (loadedState.Value is not { } state)
        {
            return console.Fail(loadedState.Error);
        }

        if (!SourceNameExists(state.Configuration, name))
        {
            return console.Fail($"Source '{name}' is not configured.");
        }

        var consumers = FindLockConsumers(state.LockFile, name);
        if (consumers.Count > 0)
        {
            return console.Fail(
                $"Source '{name}' is still used by {string.Join(", ", consumers)}. Uninstall or move these packs before removing the source."
            );
        }

        state.Configuration.Sources.RemoveAll(source =>
            string.Equals(source.Name, name, StringComparison.Ordinal)
        );

        state.Configuration.Trust.Sources.RemoveAll(source =>
            string.Equals(source, name, StringComparison.Ordinal)
        );
        state.Configuration.Trust.Packs.RemoveAll(pack =>
            string.Equals(pack.Source, name, StringComparison.Ordinal)
        );
        var savedState = await projectStateStore.SaveAllowingUnavailableSourcesAsync(
            projectDirectory,
            state
        );
        if (!savedState.IsSuccess)
        {
            return console.Fail(savedState.Error);
        }

        console.Success($"✓ Source '{name}' removed");
        if (state.Configuration.Sources.Count == 0)
        {
            console.Info(string.Empty);
            console.Info("No sources remain.");
            nextStepRenderer.Render(nextStepAdvisor.Recommend(NextStepContext.NoSourcesRemain));
        }
        else
        {
            nextStepRenderer.Render(
                nextStepAdvisor.Recommend(NextStepContext.SourcesRemain),
                "Suggested commands:"
            );
        }

        return 0;
    }

    public async Task<int> RenameAsync(string projectDirectory, string currentName, string newName)
    {
        if (string.IsNullOrWhiteSpace(newName))
        {
            return console.Fail("A new source name is required.");
        }

        var loadedState = await projectStateStore.LoadAsync(projectDirectory);
        if (loadedState.Value is not { } state)
        {
            return console.Fail(loadedState.Error);
        }

        var source = state.Configuration.Sources.Find(configured =>
            string.Equals(configured.Name, currentName, StringComparison.Ordinal)
        );
        if (source is null)
        {
            return console.Fail($"Source '{currentName}' is not configured.");
        }

        if (SourceNameExists(state.Configuration, newName))
        {
            return console.Fail($"Source name '{newName}' is already configured.");
        }

        source.Name = newName;
        RenameTrustReferences(state.Configuration, currentName, newName);
        RenameLockReferences(state.LockFile, currentName, newName);

        var savedState = await projectStateStore.SaveAllowingUnavailableSourcesAsync(
            projectDirectory,
            state
        );
        if (!savedState.IsSuccess)
        {
            return console.Fail(savedState.Error);
        }

        console.Info($"✓ Source '{currentName}' renamed to '{newName}'");
        nextStepRenderer.Render(
            nextStepAdvisor.Recommend(NextStepContext.SourcesRemain),
            "Suggested commands:"
        );
        return 0;
    }

    private static void RenameTrustReferences(
        ProjectConfiguration configuration,
        string currentName,
        string newName
    )
    {
        for (var index = 0; index < configuration.Trust.Sources.Count; index++)
        {
            if (
                string.Equals(
                    configuration.Trust.Sources[index],
                    currentName,
                    StringComparison.Ordinal
                )
            )
            {
                configuration.Trust.Sources[index] = newName;
            }
        }

        for (var index = 0; index < configuration.Trust.Packs.Count; index++)
        {
            var trustedPack = configuration.Trust.Packs[index];
            if (string.Equals(trustedPack.Source, currentName, StringComparison.Ordinal))
            {
                configuration.Trust.Packs[index] = trustedPack with { Source = newName };
            }
        }
    }

    private static void RenameLockReferences(
        ProjectLockFile lockFile,
        string currentName,
        string newName
    )
    {
        foreach (var resolvedPack in lockFile.Packs)
        {
            if (string.Equals(resolvedPack.SourceName, currentName, StringComparison.Ordinal))
            {
                resolvedPack.SourceName = newName;
            }

            foreach (var (alias, externalSource) in resolvedPack.ExternalSources)
            {
                if (string.Equals(externalSource.SourceName, currentName, StringComparison.Ordinal))
                {
                    resolvedPack.ExternalSources[alias] = externalSource with
                    {
                        SourceName = newName,
                    };
                }
            }

            foreach (var managedFile in resolvedPack.ManagedFiles)
            {
                if (string.Equals(managedFile.SourceName, currentName, StringComparison.Ordinal))
                {
                    managedFile.SourceName = newName;
                }
            }
        }
    }

    private static List<string> FindLockConsumers(ProjectLockFile lockFile, string name)
    {
        var consumers = new List<string>();
        foreach (var resolvedPack in lockFile.Packs)
        {
            if (string.Equals(resolvedPack.SourceName, name, StringComparison.Ordinal))
            {
                consumers.Add($"pack '{resolvedPack.Id}'");
                continue;
            }

            if (
                resolvedPack.ExternalSources.Values.Any(externalSource =>
                    string.Equals(externalSource.SourceName, name, StringComparison.Ordinal)
                )
            )
            {
                consumers.Add($"pack '{resolvedPack.Id}' external content");
            }
        }

        return consumers;
    }

    private int CompleteSourceAddition(ManifestOperationResult<bool> savedState, string name)
    {
        if (!savedState.IsSuccess)
        {
            return console.Fail(savedState.Error);
        }

        console.Success($"✓ Source '{name}' added");
        nextStepRenderer.Render(nextStepAdvisor.Recommend(NextStepContext.SourceAdded));
        return 0;
    }

    private bool IsSafeRepositoryPath(string? path) =>
        path is null
        || (
            !fileSystem.Path.IsPathRooted(path)
            && path.Split(['/', '\\'])
                .All(segment => !string.Equals(segment, "..", StringComparison.Ordinal))
        );

    private static bool SourceNameExists(ProjectConfiguration configuration, string name) =>
        configuration.Sources.Exists(source =>
            string.Equals(source.Name, name, StringComparison.Ordinal)
        );
}
