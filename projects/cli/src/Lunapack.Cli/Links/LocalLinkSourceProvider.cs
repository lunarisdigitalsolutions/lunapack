using System.IO.Abstractions;
using Lunapack.Cli.Application.CommandExecution;
using Lunapack.Cli.Application.Paths;
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

        return Task.FromResult(List(projectDirectory, localSource, link, cancellationToken));
    }

    private ManifestOperationResult<LinkSourceListing> List(
        string projectDirectory,
        ProjectConfiguration.LocalSource localSource,
        ProjectConfiguration.Link link,
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

        var paths = EnumerateRegularFiles(rootDirectory, cancellationToken);
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
                paths.Error ?? $"Unable to read link source '{localSource.Name}'."
            );
    }

    public Task<ManifestOperationResult<IReadOnlyDictionary<string, string>>> MaterializeAsync(
        LinkSourceListing listing,
        IReadOnlyList<string> selectedPaths,
        LinkOperationWorkspace workspace,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(listing);
        ArgumentNullException.ThrowIfNull(selectedPaths);
        ArgumentNullException.ThrowIfNull(workspace);

        var snapshots = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var sourcePath in selectedPaths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var filePath = fileSystem.Path.Combine(
                listing.RootDirectory,
                sourcePath.Replace('/', fileSystem.Path.DirectorySeparatorChar)
            );
            if (!fileSystem.File.Exists(filePath))
            {
                return Task.FromResult(
                    ManifestOperationResult<IReadOnlyDictionary<string, string>>.Failure(
                        $"Link source file '{sourcePath}' is no longer available."
                    )
                );
            }

            snapshots.Add(
                sourcePath,
                workspace.Write(sourcePath, fileSystem.File.ReadAllBytes(filePath))
            );
        }

        return Task.FromResult(
            ManifestOperationResult<IReadOnlyDictionary<string, string>>.Success(snapshots)
        );
    }

    private ManifestOperationResult<IReadOnlyList<string>> EnumerateRegularFiles(
        string rootDirectory,
        CancellationToken cancellationToken
    )
    {
        var paths = new List<string>();
        var filePaths = fileSystem.Directory.EnumerateFiles(
            rootDirectory,
            "*",
            SearchOption.AllDirectories
        );
        foreach (var filePath in filePaths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var fileInfo = fileSystem.FileInfo.New(filePath);
            if (!fileInfo.Exists || fileInfo.LinkTarget is not null)
            {
                continue;
            }

            var relativePath = ProjectPath.Normalize(
                fileSystem.Path.GetRelativePath(rootDirectory, filePath)
            );
            if (relativePath.StartsWith("../", StringComparison.Ordinal))
            {
                return ManifestOperationResult<IReadOnlyList<string>>.Failure(
                    $"Link source contains path '{relativePath}' outside the source root."
                );
            }

            paths.Add(relativePath);
        }

        return ManifestOperationResult<IReadOnlyList<string>>.Success(paths);
    }
}
