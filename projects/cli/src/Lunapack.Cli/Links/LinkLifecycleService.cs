using System.IO.Abstractions;
using System.Security.Cryptography;

namespace Lunapack.Cli;

internal sealed class LinkLifecycleService(
    IFileSystem fileSystem,
    LinkResolver linkResolver,
    LinkPlanner linkPlanner,
    PackUpdateTransaction updateTransaction,
    IProjectStateStore projectStateStore,
    CliConsole console
)
{
    public async Task<int> InstallAsync(
        string projectDirectory,
        string linkName,
        bool adoptExisting = false,
        bool allowReinstall = false,
        ProjectState? preparedState = null,
        CancellationToken cancellationToken = default
    )
    {
        var state = preparedState;
        if (state is null)
        {
            var loadedState = await projectStateStore.LoadAsync(projectDirectory);
            if (loadedState.Value is not { } loaded)
            {
                return console.Fail(loadedState.Error ?? "Unable to load project state.");
            }

            state = loaded;
        }

        if (!state.Configuration.Links.TryGetValue(linkName, out var definition))
        {
            return console.Fail($"Link '{linkName}' is not configured.");
        }

        if (!allowReinstall && state.LockFile.Links.ContainsKey(linkName))
        {
            return console.Fail($"Link '{linkName}' is already installed.");
        }

        console.Info($"Installing link '{linkName}'.");
        return await ResolveApplyAndSaveAsync(
            projectDirectory,
            linkName,
            definition,
            adoptExisting,
            useLockedIdentity: false,
            state,
            cancellationToken
        );
    }

    public async Task<int> UpdateAsync(
        string projectDirectory,
        string? linkName = null,
        CancellationToken cancellationToken = default
    )
    {
        var loadedState = await projectStateStore.LoadAsync(projectDirectory);
        if (loadedState.Value is not { } state)
        {
            return console.Fail(loadedState.Error ?? "Unable to load project state.");
        }

        var names = SelectLinkNames(state, linkName);
        if (names.Value is not { } linkNames)
        {
            return console.Fail(names.Error);
        }

        foreach (var name in linkNames)
        {
            console.Info($"Updating link '{name}'.");
            var updated = await ResolveApplyAndSaveAsync(
                projectDirectory,
                name,
                state.Configuration.Links[name],
                adoptExisting: false,
                useLockedIdentity: true,
                cancellationToken: cancellationToken
            );
            if (updated != 0)
            {
                return updated;
            }
        }

        return 0;
    }

    public async Task<ManifestOperationResult<IReadOnlyList<LinkOutdatedReport>>> OutdatedAsync(
        string projectDirectory,
        CancellationToken cancellationToken = default
    )
    {
        var loadedState = await projectStateStore.LoadAsync(projectDirectory);
        if (loadedState.Value is not { } state)
        {
            return ManifestOperationResult<IReadOnlyList<LinkOutdatedReport>>.Failure(
                loadedState.Error ?? "Unable to load project state."
            );
        }

        var reports = new List<LinkOutdatedReport>();
        foreach (var (name, definition) in OrderedLinks(state.Configuration))
        {
            state.LockFile.Links.TryGetValue(name, out var lockedLink);
            if (lockedLink is null)
            {
                reports.Add(new LinkOutdatedReport(name, ["not installed"]));
                continue;
            }

            var resolution = await linkResolver.ResolveAsync(
                projectDirectory,
                state.Configuration,
                name,
                definition,
                lockedLink.SourceIdentity,
                cancellationToken
            );
            if (resolution.Value is not { } resolved)
            {
                return ManifestOperationResult<IReadOnlyList<LinkOutdatedReport>>.Failure(
                    resolution.Error ?? $"Unable to resolve link '{name}'."
                );
            }

            using var scope = resolved;
            var diff = LinkDiffCalculator.Compare(lockedLink, resolved.Snapshot);
            if (!diff.IsCurrent)
            {
                reports.Add(new LinkOutdatedReport(name, diff.DescribeReasons()));
            }
        }

        return ManifestOperationResult<IReadOnlyList<LinkOutdatedReport>>.Success(reports);
    }

    public IReadOnlyList<LinkAuditReport> Audit(string projectDirectory, ProjectLockFile lockFile)
    {
        ArgumentNullException.ThrowIfNull(lockFile);

        var ownership = ManagedRootInventory.CreateOwnershipMap(lockFile);
        return
        [
            .. lockFile
                .Links.OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => new LinkAuditReport(
                    pair.Key,
                    pair.Value.SourceName,
                    pair.Value.GitSource?.ResolvedCommit,
                    [
                        .. pair.Value.Files.Select(file =>
                            DescribeFileStatus(
                                projectDirectory,
                                new ManagedRootOwner(ManagedRootKind.Link, pair.Key),
                                file,
                                ownership
                            )
                        ),
                    ]
                )),
        ];
    }

    public async Task<int> UninstallAsync(string projectDirectory, string linkName)
    {
        var loadedState = await projectStateStore.LoadAsync(projectDirectory);
        if (loadedState.Value is not { } state)
        {
            return console.Fail(loadedState.Error ?? "Unable to load project state.");
        }

        if (!state.LockFile.Links.TryGetValue(linkName, out var lockedLink))
        {
            return console.Fail($"Link '{linkName}' is not installed.");
        }

        console.Info($"Uninstalling link '{linkName}'.");
        if (FindModifiedTargets(projectDirectory, lockedLink) is [var modifiedTarget, ..])
        {
            return console.Fail($"Managed target '{modifiedTarget}' has changed.");
        }

        return await RemoveInstalledLinkAsync(projectDirectory, state, linkName, lockedLink);
    }

    public async Task<int> RemoveAsync(string projectDirectory, string linkName, bool force)
    {
        var loadedState = await projectStateStore.LoadAsync(projectDirectory);
        if (loadedState.Value is not { } state)
        {
            return console.Fail(loadedState.Error ?? "Unable to load project state.");
        }

        if (!state.Configuration.Links.ContainsKey(linkName))
        {
            return console.Fail($"Link '{linkName}' is not configured.");
        }

        state.LockFile.Links.TryGetValue(linkName, out var lockedLink);
        if (lockedLink is null)
        {
            state.Configuration.Links.Remove(linkName);
            var savedState = await projectStateStore.SaveAsync(projectDirectory, state);
            return savedState.IsSuccess ? 0 : console.Fail(savedState.Error);
        }

        if (!force)
        {
            return console.Fail(
                $"Link '{linkName}' is installed. Run 'luna uninstall {linkName}' or pass '--force'."
            );
        }

        return await RemoveInstalledLinkAsync(projectDirectory, state, linkName, lockedLink);
    }

    private async Task<int> ResolveApplyAndSaveAsync(
        string projectDirectory,
        string linkName,
        ProjectConfiguration.Link definition,
        bool adoptExisting,
        bool useLockedIdentity,
        ProjectState? preparedState = null,
        CancellationToken cancellationToken = default
    )
    {
        var state = preparedState;
        if (state is null)
        {
            var loadedState = await projectStateStore.LoadAsync(projectDirectory);
            if (loadedState.Value is not { } loaded)
            {
                return console.Fail(loadedState.Error ?? "Unable to load project state.");
            }

            state = loaded;
        }

        state.LockFile.Links.TryGetValue(linkName, out var lockedLink);
        var lockedIdentity = useLockedIdentity ? lockedLink?.SourceIdentity : null;
        var resolution = await linkResolver.ResolveAsync(
            projectDirectory,
            state.Configuration,
            linkName,
            definition,
            lockedIdentity,
            cancellationToken
        );
        if (resolution.Value is not { } resolved)
        {
            return console.Fail(resolution.Error ?? $"Unable to resolve link '{linkName}'.");
        }

        using var scope = resolved;
        if (
            lockedLink is not null
            && LinkDiffCalculator.Compare(lockedLink, resolved.Snapshot) is { IsCurrent: true }
            && FindModifiedTargets(projectDirectory, lockedLink).Count == 0
            && !HasMissingTargets(projectDirectory, lockedLink)
        )
        {
            return await SaveEvidenceAsync(projectDirectory, state, resolved.Snapshot);
        }

        var plan = linkPlanner.Plan(projectDirectory, resolved, state.LockFile, adoptExisting);
        if (plan.Value is not { } updatePlan)
        {
            return console.Fail(plan.Error);
        }

        return await ApplyAndSaveAsync(projectDirectory, state, resolved.Snapshot, updatePlan);
    }

    private async Task<int> SaveEvidenceAsync(
        string projectDirectory,
        ProjectState state,
        ResolvedLinkSnapshot snapshot
    )
    {
        state.LockFile.Links[snapshot.Name] = snapshot.ToLockRecord();
        var savedState = await projectStateStore.SaveAsync(projectDirectory, state);
        return savedState.IsSuccess ? 0 : console.Fail(savedState.Error);
    }

    private async Task<int> ApplyAndSaveAsync(
        string projectDirectory,
        ProjectState state,
        ResolvedLinkSnapshot snapshot,
        PackUpdatePlan updatePlan
    )
    {
        var applied = updateTransaction.Apply(updatePlan);
        if (applied.Value is not { } rollback)
        {
            return console.Fail(applied.Error);
        }

        var isPersisted = false;
        try
        {
            state.LockFile.Links[snapshot.Name] = snapshot.ToLockRecord();
            var savedState = await projectStateStore.SaveAsync(projectDirectory, state);
            if (savedState.IsSuccess)
            {
                isPersisted = true;
                return 0;
            }

            return console.Fail(savedState.Error);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return console.Fail($"Unable to update link: {exception.Message}");
        }
        finally
        {
            if (!isPersisted)
            {
                rollback.Restore();
            }
        }
    }

    private async Task<int> RemoveInstalledLinkAsync(
        string projectDirectory,
        ProjectState state,
        string linkName,
        ProjectLockFile.ResolvedLink lockedLink
    )
    {
        var owner = new ManagedRootOwner(ManagedRootKind.Link, linkName);
        var preserved = new List<string>();
        var actions = new List<PlannedPackUpdateAction>();
        foreach (var file in lockedLink.Files)
        {
            var targetPath = fileSystem.Path.GetFullPath(file.TargetPath, projectDirectory);
            if (fileSystem.File.Exists(targetPath) && !TargetMatchesDigest(targetPath, file.Sha256))
            {
                preserved.Add(file.TargetPath);
                continue;
            }

            actions.Add(
                new DeleteManagedFileUpdateAction(owner, ToManagedRootFile(file), targetPath)
            );
        }

        var applied = updateTransaction.Apply(new PackUpdatePlan(actions));
        if (applied.Value is not { } rollback)
        {
            return console.Fail(applied.Error);
        }

        state.Configuration.Links.Remove(linkName);
        state.LockFile.Links.Remove(linkName);
        var savedState = await projectStateStore.SaveAsync(projectDirectory, state);
        if (!savedState.IsSuccess)
        {
            rollback.Restore();
            return console.Fail(savedState.Error);
        }

        foreach (var target in preserved)
        {
            console.Warning($"Preserved locally modified target '{target}'.");
        }

        return 0;
    }

    private static ManagedRootFile ToManagedRootFile(ProjectLockFile.LinkFile file) =>
        new(file.SourcePath, file.DeclaredTargetPath, file.TargetPath, file.Sha256);

    private static IEnumerable<KeyValuePair<string, ProjectConfiguration.Link>> OrderedLinks(
        ProjectConfiguration configuration
    ) => configuration.Links.OrderBy(pair => pair.Key, StringComparer.Ordinal);

    private static ManifestOperationResult<List<string>> SelectLinkNames(
        ProjectState state,
        string? linkName
    )
    {
        if (linkName is null)
        {
            return ManifestOperationResult<List<string>>.Success([
                .. OrderedLinks(state.Configuration).Select(pair => pair.Key),
            ]);
        }

        return state.Configuration.Links.ContainsKey(linkName)
            ? ManifestOperationResult<List<string>>.Success([linkName])
            : ManifestOperationResult<List<string>>.Failure(
                $"Link '{linkName}' is not configured."
            );
    }

    private bool HasMissingTargets(
        string projectDirectory,
        ProjectLockFile.ResolvedLink lockedLink
    ) =>
        lockedLink.Files.Exists(file =>
            !fileSystem.File.Exists(fileSystem.Path.GetFullPath(file.TargetPath, projectDirectory))
        );

    private List<string> FindModifiedTargets(
        string projectDirectory,
        ProjectLockFile.ResolvedLink lockedLink
    ) =>
        [
            .. lockedLink
                .Files.Where(file =>
                {
                    var targetPath = fileSystem.Path.GetFullPath(file.TargetPath, projectDirectory);
                    return fileSystem.File.Exists(targetPath)
                        && !TargetMatchesDigest(targetPath, file.Sha256);
                })
                .Select(file => file.TargetPath),
        ];

    private LinkFileAuditStatus DescribeFileStatus(
        string projectDirectory,
        ManagedRootOwner owner,
        ProjectLockFile.LinkFile file,
        Dictionary<string, List<ManagedRootOwner>> ownership
    )
    {
        if (
            ownership.TryGetValue(file.TargetPath, out var owners)
            && owners.Exists(candidate => !candidate.Matches(owner))
        )
        {
            return new LinkFileAuditStatus(file.TargetPath, "conflicting");
        }

        var targetPath = fileSystem.Path.GetFullPath(file.TargetPath, projectDirectory);
        if (!fileSystem.File.Exists(targetPath))
        {
            return new LinkFileAuditStatus(file.TargetPath, "missing");
        }

        return TargetMatchesDigest(targetPath, file.Sha256)
            ? new LinkFileAuditStatus(file.TargetPath, "ok")
            : new LinkFileAuditStatus(file.TargetPath, "modified");
    }

    private bool TargetMatchesDigest(string targetPath, string sha256) =>
        string.Equals(
            Convert.ToHexString(SHA256.HashData(fileSystem.File.ReadAllBytes(targetPath))),
            sha256,
            StringComparison.OrdinalIgnoreCase
        );
}
