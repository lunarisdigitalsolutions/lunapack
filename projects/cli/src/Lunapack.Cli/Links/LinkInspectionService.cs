using System.IO.Abstractions;
using System.Security.Cryptography;

namespace Lunapack.Cli;

internal sealed class LinkInspectionService(
    IFileSystem fileSystem,
    IProjectStateStore projectStateStore
)
{
    public async Task<ManifestOperationResult<IReadOnlyList<LinkSummary>>> ListAsync(
        string projectDirectory
    )
    {
        var loadedState = await projectStateStore.LoadAsync(projectDirectory);
        if (loadedState.Value is not { } state)
        {
            return ManifestOperationResult<IReadOnlyList<LinkSummary>>.Failure(
                loadedState.Error ?? "Unable to load project state."
            );
        }

        return ManifestOperationResult<IReadOnlyList<LinkSummary>>.Success([
            .. state
                .Configuration.Links.OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => CreateSummary(projectDirectory, state, pair.Key, pair.Value)),
        ]);
    }

    public async Task<ManifestOperationResult<LinkDetail>> ShowAsync(
        string projectDirectory,
        string linkName
    )
    {
        var loadedState = await projectStateStore.LoadAsync(projectDirectory);
        if (loadedState.Value is not { } state)
        {
            return ManifestOperationResult<LinkDetail>.Failure(
                loadedState.Error ?? "Unable to load project state."
            );
        }

        if (!state.Configuration.Links.TryGetValue(linkName, out var link))
        {
            return ManifestOperationResult<LinkDetail>.Failure(
                $"Link '{linkName}' is not configured."
            );
        }

        state.LockFile.Links.TryGetValue(linkName, out var lockedLink);
        var configuredSource = state.Configuration.Sources.Find(source =>
            string.Equals(source.Name, link.Source, StringComparison.Ordinal)
        );
        return ManifestOperationResult<LinkDetail>.Success(
            new LinkDetail(
                CreateSummary(projectDirectory, state, linkName, link),
                link.Ref ?? (configuredSource as ProjectConfiguration.GitSource)?.Ref,
                lockedLink?.GitSource?.ResolvedCommit,
                link.Path ?? LinkSummary.WorkspaceRootTarget,
                link.Includes,
                link.Excludes,
                link.Flatten ?? false,
                link.StripPrefix
            )
        );
    }

    private LinkSummary CreateSummary(
        string projectDirectory,
        ProjectState state,
        string name,
        ProjectConfiguration.Link link
    )
    {
        state.LockFile.Links.TryGetValue(name, out var lockedLink);
        return new LinkSummary(
            name,
            link.Source,
            link.Target ?? LinkSummary.WorkspaceRootTarget,
            lockedLink is not null,
            lockedLink?.Files.Count ?? 0,
            lockedLink is null ? 0 : CountModifiedFiles(projectDirectory, lockedLink)
        );
    }

    private int CountModifiedFiles(
        string projectDirectory,
        ProjectLockFile.ResolvedLink lockedLink
    ) =>
        lockedLink.Files.Count(file =>
        {
            var targetPath = fileSystem.Path.GetFullPath(file.TargetPath, projectDirectory);
            return !fileSystem.File.Exists(targetPath)
                || !string.Equals(
                    Convert.ToHexString(SHA256.HashData(fileSystem.File.ReadAllBytes(targetPath))),
                    file.Sha256,
                    StringComparison.OrdinalIgnoreCase
                );
        });
}
