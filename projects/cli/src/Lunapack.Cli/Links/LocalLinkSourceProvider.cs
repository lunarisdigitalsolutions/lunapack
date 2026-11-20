using System.IO.Abstractions;
using Lunapack.Cli.Application.CommandExecution;
using Lunapack.Cli.Application.Paths;
using Lunapack.Cli.Application.Preferences;
using Lunapack.Cli.Project;
using Lunapack.Cli.Sources;

namespace Lunapack.Cli.Links;

internal sealed class LocalLinkSourceProvider(IFileSystem fileSystem) : ILinkSourceProvider
{
    public bool CanProvide(ProjectConfiguration.Source source) =>
        source is ProjectConfiguration.LocalSource;

    public Task<ManifestOperationResult<LinkSourceListing>> ListAsync(
        string projectDirectory,
        ProjectConfiguration.Source source,
        ProjectConfiguration.Link link,
        ConfiguredSourceIdentity? lockedIdentity,
        CancellationToken cancellationToken
    ) => ListAsync(projectDirectory, source, link, lockedIdentity, null, cancellationToken);

    public Task<ManifestOperationResult<LinkSourceListing>> ListAsync(
        string projectDirectory,
        ProjectConfiguration.Source source,
        ProjectConfiguration.Link link,
        ConfiguredSourceIdentity? lockedIdentity,
        ResourceLimits? resourceLimits = null,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(link);

        if (source is not ProjectConfiguration.LocalSource localSource)
        {
            return Task.FromResult(
                ManifestOperationResult<LinkSourceListing>.Failure(
                    "Local link resolution requires a local source."
                )
            );
        }

        return Task.FromResult(
            List(
                projectDirectory,
                localSource,
                link,
                resourceLimits ?? ResourceLimits.Default,
                cancellationToken
            )
        );
    }

    private ManifestOperationResult<LinkSourceListing> List(
        string projectDirectory,
        ProjectConfiguration.LocalSource localSource,
        ProjectConfiguration.Link link,
        ResourceLimits resourceLimits,
        CancellationToken cancellationToken
    )
    {
        if (link.Ref is not null)
        {
            return ManifestOperationResult<LinkSourceListing>.Failure(
                $"Link source '{localSource.Name}' is a local source and does not support refs."
            );
        }

        var rootDirectory = fileSystem.Path.GetFullPath(
            localSource.Path,
            fileSystem.Path.GetFullPath(projectDirectory)
        );
        if (!fileSystem.Directory.Exists(rootDirectory))
        {
            return ManifestOperationResult<LinkSourceListing>.Failure(
                $"Link source directory '{localSource.Path}' does not exist."
            );
        }

        var rootValidation = ValidateRootComponents(
            fileSystem.Path.GetFullPath(projectDirectory),
            rootDirectory
        );
        if (!rootValidation.IsSuccess)
        {
            return ManifestOperationResult<LinkSourceListing>.Failure(
                rootValidation.Error!,
                rootValidation.ErrorKind
            );
        }

        var paths = EnumerateRegularFiles(rootDirectory, resourceLimits, cancellationToken);
        return paths.Value is { } sourcePaths
            ? ManifestOperationResult<LinkSourceListing>.Success(
                new LinkSourceListing(
                    ConfiguredSourceIdentity.Create(localSource),
                    null,
                    rootDirectory,
                    sourcePaths
                )
            )
            : ManifestOperationResult<LinkSourceListing>.Failure(
                paths.Error ?? $"Unable to read link source '{localSource.Name}'.",
                paths.ErrorKind
            );
    }

    public Task<ManifestOperationResult<IReadOnlyDictionary<string, string>>> MaterializeAsync(
        LinkSourceListing listing,
        IReadOnlyList<string> selectedPaths,
        LinkOperationWorkspace workspace,
        CancellationToken cancellationToken
    ) => MaterializeAsync(listing, selectedPaths, workspace, null, cancellationToken);

    public async Task<
        ManifestOperationResult<IReadOnlyDictionary<string, string>>
    > MaterializeAsync(
        LinkSourceListing listing,
        IReadOnlyList<string> selectedPaths,
        LinkOperationWorkspace workspace,
        ResourceLimits? resourceLimits = null,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(listing);
        ArgumentNullException.ThrowIfNull(selectedPaths);
        ArgumentNullException.ThrowIfNull(workspace);

        var limits = resourceLimits ?? ResourceLimits.Default;
        var budget = new LinkSnapshotBudget(limits);
        var snapshots = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var sourcePath in selectedPaths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var validatedPath = ValidateMaterializationPath(listing.RootDirectory, sourcePath);
            if (validatedPath.Value is not { } resolvedFilePath)
            {
                return ManifestOperationResult<IReadOnlyDictionary<string, string>>.Failure(
                    validatedPath.Error ?? $"Link source file '{sourcePath}' is unavailable.",
                    validatedPath.ErrorKind
                );
            }

            var length = fileSystem.FileInfo.New(resolvedFilePath).Length;
            var observed = budget.AddFilePath(sourcePath, length);
            if (!observed.IsSuccess)
            {
                return ManifestOperationResult<IReadOnlyDictionary<string, string>>.Failure(
                    observed.Error!,
                    observed.ErrorKind
                );
            }

            await using var source = fileSystem.File.OpenRead(resolvedFilePath);
            var snapshot = await workspace.WriteAsync(
                sourcePath,
                source,
                length,
                cancellationToken
            );
            if (snapshot.Value is not { } snapshotPath)
            {
                return ManifestOperationResult<IReadOnlyDictionary<string, string>>.Failure(
                    snapshot.Error ?? $"Unable to snapshot link source file '{sourcePath}'.",
                    snapshot.ErrorKind
                );
            }

            snapshots.Add(sourcePath, snapshotPath);
        }

        return ManifestOperationResult<IReadOnlyDictionary<string, string>>.Success(snapshots);
    }

    private ManifestOperationResult<IReadOnlyList<string>> EnumerateRegularFiles(
        string rootDirectory,
        ResourceLimits resourceLimits,
        CancellationToken cancellationToken
    )
    {
        var paths = new List<string>();
        var entriesObserved = 0L;
        var pendingDirectories = new Stack<string>();
        pendingDirectories.Push(rootDirectory);
        while (pendingDirectories.TryPop(out var directory))
        {
            foreach (
                var entry in fileSystem.DirectoryInfo.New(directory).EnumerateFileSystemInfos()
            )
            {
                cancellationToken.ThrowIfCancellationRequested();
                entriesObserved++;
                if (entriesObserved > resourceLimits.FilesPerGraph)
                {
                    return ManifestOperationResult<IReadOnlyList<string>>.Failure(
                        $"Link source exceeds its {resourceLimits.FilesPerGraph} file and directory limit (at least {entriesObserved} entries observed).",
                        ManifestOperationErrorKind.ResourceLimitExceeded
                    );
                }

                if (entry.Attributes.HasFlag(FileAttributes.ReparsePoint))
                {
                    var relativeReparsePath = ProjectPath.Normalize(
                        fileSystem.Path.GetRelativePath(rootDirectory, entry.FullName)
                    );
                    return ManifestOperationResult<IReadOnlyList<string>>.Failure(
                        $"Link source contains a symbolic link or reparse point at '{relativeReparsePath}'."
                    );
                }

                if (entry.Attributes.HasFlag(FileAttributes.Directory))
                {
                    pendingDirectories.Push(entry.FullName);
                    continue;
                }

                var relativePath = ProjectPath.Normalize(
                    fileSystem.Path.GetRelativePath(rootDirectory, entry.FullName)
                );
                if (relativePath.StartsWith("../", StringComparison.Ordinal))
                {
                    return ManifestOperationResult<IReadOnlyList<string>>.Failure(
                        $"Link source contains path '{relativePath}' outside the source root."
                    );
                }

                paths.Add(relativePath);
            }
        }

        return ManifestOperationResult<IReadOnlyList<string>>.Success(paths);
    }

    private ManifestOperationResult<bool> ValidateRootComponents(
        string projectDirectory,
        string rootDirectory
    )
    {
        var projectPath = fileSystem.Path.TrimEndingDirectorySeparator(
            fileSystem.Path.GetFullPath(projectDirectory)
        );
        var rootPath = fileSystem.Path.TrimEndingDirectorySeparator(
            fileSystem.Path.GetFullPath(rootDirectory)
        );
        var relative = fileSystem.Path.GetRelativePath(projectPath, rootPath);
        if (
            fileSystem.Path.IsPathRooted(relative)
            || string.Equals(relative, "..", StringComparison.Ordinal)
            || relative.StartsWith(
                $"..{fileSystem.Path.DirectorySeparatorChar}",
                StringComparison.Ordinal
            )
        )
        {
            return ManifestOperationResult<bool>.Failure(
                "Local link source root is outside the project directory."
            );
        }

        var current = projectPath;
        foreach (
            var segment in relative.Split(
                fileSystem.Path.DirectorySeparatorChar,
                fileSystem.Path.AltDirectorySeparatorChar
            )
        )
        {
            if (
                string.IsNullOrEmpty(segment)
                || string.Equals(segment, ".", StringComparison.Ordinal)
            )
            {
                continue;
            }

            current = fileSystem.Path.Combine(current, segment);
            if (fileSystem.File.GetAttributes(current).HasFlag(FileAttributes.ReparsePoint))
            {
                return ManifestOperationResult<bool>.Failure(
                    $"Local link source root contains a symbolic link or reparse point at '{ProjectPath.Normalize(fileSystem.Path.GetRelativePath(projectPath, current))}'."
                );
            }
        }

        return ManifestOperationResult<bool>.Success(true);
    }

    private ManifestOperationResult<string> ValidateMaterializationPath(
        string rootDirectory,
        string sourcePath
    )
    {
        var normalized = ProjectPath.NormalizeProjectRelativePath(
            fileSystem,
            rootDirectory,
            sourcePath
        );
        if (normalized.Value is not { } relativePath)
        {
            return ManifestOperationResult<string>.Failure(
                normalized.Error ?? $"Link source path '{sourcePath}' is invalid."
            );
        }

        var current = rootDirectory;
        foreach (var segment in relativePath.Split('/'))
        {
            current = fileSystem.Path.Combine(current, segment);
            if (!fileSystem.File.Exists(current) && !fileSystem.Directory.Exists(current))
            {
                return ManifestOperationResult<string>.Failure(
                    $"Link source file '{sourcePath}' is no longer available."
                );
            }

            if (fileSystem.File.GetAttributes(current).HasFlag(FileAttributes.ReparsePoint))
            {
                return ManifestOperationResult<string>.Failure(
                    $"Link source file '{sourcePath}' contains a symbolic link or reparse point."
                );
            }
        }

        return fileSystem.File.Exists(current)
            ? ManifestOperationResult<string>.Success(current)
            : ManifestOperationResult<string>.Failure(
                $"Link source path '{sourcePath}' is not a regular file."
            );
    }
}
