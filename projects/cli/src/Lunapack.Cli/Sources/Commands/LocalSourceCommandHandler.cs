using System.CommandLine;
using System.IO.Abstractions;

namespace Lunapack.Cli;

internal sealed class LocalSourceCommandHandler(
    IFileSystem fileSystem,
    ProjectStateStore projectStateStore,
    WorkspaceDirectoryResolver workspaceDirectoryResolver,
    CliConsole console
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
                || !TryCreateGitHubUrl(repository, out var repositoryUrl)
            )
            {
                return console.Fail(
                    "A GitHub repository must use the organization/repository format."
                );
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

        return new Command("sources", "Manage pack sources.")
        {
            addSourceCommand,
            listSourcesCommand,
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

        var sourceIsConfigured = state.Configuration.Sources.Exists(source =>
            source is ProjectConfiguration.LocalSource localSource
            && string.Equals(localSource.Path, normalizedPath, StringComparison.Ordinal)
        );
        if (sourceIsConfigured)
        {
            return console.Fail($"Local source '{normalizedPath}' is already configured.");
        }

        state.Configuration.Sources.Add(
            new ProjectConfiguration.LocalSource { Name = name, Path = normalizedPath }
        );
        var savedState = await projectStateStore.SaveAsync(projectDirectory, state);

        return savedState.IsSuccess ? 0 : console.Fail(savedState.Error);
    }

    public async Task<int> AddGitSourceAsync(
        string projectDirectory,
        string name,
        string repositoryUrl,
        string? gitRef,
        string? repositoryPath
    )
    {
        if (string.IsNullOrEmpty(name))
        {
            return console.Fail("A source name is required.");
        }

        if (string.IsNullOrWhiteSpace(repositoryUrl))
        {
            return console.Fail("A Git repository URL is required.");
        }

        var normalizedRepositoryPath = ProjectPath.NormalizeOptional(repositoryPath);
        if (!IsSafeRepositoryPath(normalizedRepositoryPath))
        {
            return console.Fail(
                "Git source paths must be repository-relative and must not contain '..'."
            );
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

        var sourceIsConfigured = state.Configuration.Sources.Exists(source =>
            source is ProjectConfiguration.GitSource gitSource
            && string.Equals(gitSource.Url, repositoryUrl, StringComparison.Ordinal)
            && string.Equals(gitSource.Ref, gitRef, StringComparison.Ordinal)
            && string.Equals(gitSource.Path, normalizedRepositoryPath, StringComparison.Ordinal)
        );
        if (sourceIsConfigured)
        {
            return console.Fail($"Git source '{repositoryUrl}' is already configured.");
        }

        state.Configuration.Sources.Add(
            new ProjectConfiguration.GitSource
            {
                Name = name,
                Url = repositoryUrl,
                Ref = gitRef,
                Path = normalizedRepositoryPath,
            }
        );
        var savedState = await projectStateStore.SaveAsync(projectDirectory, state);

        return savedState.IsSuccess ? 0 : console.Fail(savedState.Error);
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

    private static bool TryCreateGitHubUrl(string repository, out string repositoryUrl)
    {
        var segments = repository.Split('/', StringSplitOptions.None);
        if (
            segments.Length != 2
            || segments.Any(segment =>
                string.IsNullOrEmpty(segment)
                || segment.Any(character =>
                    !char.IsLetterOrDigit(character) && character is not '-' and not '_' and not '.'
                )
            )
        )
        {
            repositoryUrl = string.Empty;
            return false;
        }

        repositoryUrl = $"https://github.com/{repository}.git";
        return true;
    }
}
