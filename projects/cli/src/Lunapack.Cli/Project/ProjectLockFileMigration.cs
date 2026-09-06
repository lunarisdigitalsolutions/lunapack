using Lunapack.Cli.Application.CommandExecution;

namespace Lunapack.Cli.Project;

internal static class ProjectLockFileMigration
{
    public const int CurrentSchemaVersion = 2;

    public static ManifestOperationResult<ProjectLockFile> ToCurrent(
        ProjectConfiguration configuration,
        ProjectLockFile lockFile
    ) =>
        lockFile.SchemaVersion switch
        {
            CurrentSchemaVersion => ManifestOperationResult<ProjectLockFile>.Success(lockFile),
            1 => MigrateVersionOne(configuration, lockFile),
            _ => ManifestOperationResult<ProjectLockFile>.Failure(
                $"Project lock file schema version must be 1 or {CurrentSchemaVersion}."
            ),
        };

    public static ManifestOperationResult<ProjectLockFile> PrepareForPersistence(
        ProjectConfiguration configuration,
        ProjectLockFile lockFile
    )
    {
        var current = ToCurrent(configuration, lockFile);
        if (current.Value is not { } currentLockFile)
        {
            return current;
        }

        var keyMigrationError = AddResolutionKeys(currentLockFile);
        if (keyMigrationError is not null)
        {
            return Failure(keyMigrationError);
        }

        var instanceMigrationError = EnsureMissingInstances(configuration, currentLockFile);
        if (instanceMigrationError is not null)
        {
            return Failure(instanceMigrationError);
        }

        var ownershipError = MoveRootOwnershipToInstances(configuration, currentLockFile);
        return ownershipError is null ? current : Failure(ownershipError);
    }

    private static ManifestOperationResult<ProjectLockFile> MigrateVersionOne(
        ProjectConfiguration configuration,
        ProjectLockFile lockFile
    )
    {
        var keyMigrationError = AddResolutionKeys(lockFile);
        if (keyMigrationError is not null)
        {
            return Failure(keyMigrationError);
        }

        var instanceMigrationError = TryCreateInstances(configuration, lockFile, out var instances);
        if (instanceMigrationError is not null)
        {
            return Failure(instanceMigrationError);
        }

        return ManifestOperationResult<ProjectLockFile>.Success(
            lockFile with
            {
                Instances = instances,
                SchemaVersion = CurrentSchemaVersion,
            }
        );
    }

    private static string? AddResolutionKeys(ProjectLockFile lockFile)
    {
        var packsByIdentity =
            new Dictionary<(string Id, string Version), ProjectLockFile.ResolvedPack>();
        foreach (var pack in lockFile.Packs)
        {
            if (pack.SourceIdentity is null)
            {
                return "Legacy lock state contains a resolved pack without source identity.";
            }

            if (!packsByIdentity.TryAdd((pack.Id, pack.Version), pack))
            {
                return $"Legacy lock state contains ambiguous resolutions for '{pack.Id}@{pack.Version}'.";
            }

            pack.Key ??= CreateKey(pack);
        }

        foreach (var pack in lockFile.Packs)
        {
            foreach (var reference in pack.Packs)
            {
                if (reference.Resolution is not null)
                {
                    continue;
                }

                if (!packsByIdentity.TryGetValue((reference.Id, reference.Version), out var target))
                {
                    return $"Legacy lock reference '{reference.Id}@{reference.Version}' cannot be correlated.";
                }

                reference.Resolution = target.Key;
            }
        }

        return null;
    }

    private static string? EnsureMissingInstances(
        ProjectConfiguration configuration,
        ProjectLockFile lockFile
    )
    {
        var instanceIdentities = lockFile
            .Instances.Select(instance => new PackInstanceIdentity(instance.Id, instance.Name))
            .ToHashSet();
        foreach (var requestedPack in configuration.Packs)
        {
            var identity = requestedPack.GetInstanceIdentity();
            if (instanceIdentities.Contains(identity))
            {
                continue;
            }

            var matches = lockFile
                .Packs.Where(pack =>
                    string.Equals(pack.Id, requestedPack.Id, StringComparison.Ordinal)
                    && (
                        requestedPack.Version is null
                        || string.Equals(
                            pack.Version,
                            requestedPack.Version,
                            StringComparison.Ordinal
                        )
                    )
                )
                .ToList();
            if (matches.Count != 1 || matches[0].Key is not { } rootResolution)
            {
                return $"Lock state cannot unambiguously correlate requested pack '{requestedPack.Id}/{identity.Alias}'.";
            }

            lockFile.Instances.Add(
                new ProjectLockFile.PackInstance
                {
                    Destination = requestedPack.Destination,
                    Id = requestedPack.Id,
                    Name = identity.Alias,
                    RootResolution = rootResolution,
                }
            );
            instanceIdentities.Add(identity);
        }

        return null;
    }

    private static string? MoveRootOwnershipToInstances(
        ProjectConfiguration configuration,
        ProjectLockFile lockFile
    )
    {
        var configurations = configuration.Packs.ToDictionary(pack => pack.GetInstanceIdentity());
        foreach (var group in lockFile.Instances.GroupBy(instance => instance.RootResolution))
        {
            var root = lockFile.Packs.SingleOrDefault(pack => pack.Key == group.Key);
            if (root is null)
            {
                return $"Lock file instance root '{group.Key.Id}@{group.Key.Version}' is unavailable.";
            }

            var instances = group.ToList();
            if (instances.Count > 1 && root.ManagedFiles.Count > 0)
            {
                return $"Resolved node '{root.Id}@{root.Version}' has ambiguous direct ownership across multiple instances.";
            }

            if (instances.Count != 1)
            {
                continue;
            }

            var instance = instances[0];
            if (root.ManagedFiles.Count > 0)
            {
                instance.ManagedFiles = root.ManagedFiles;
                instance.Placements = CreatePlacements(instance, root.ManagedFiles);
            }

            if (root.ExternalSources.Count > 0)
            {
                instance.ExternalSources = root.ExternalSources;
            }

            if (configurations.TryGetValue(new(instance.Id, instance.Name), out var requestedPack))
            {
                instance.Destination = requestedPack.Destination;
            }

            root.Destination = null;
            root.ExternalSources = [];
            root.ManagedFiles = [];
        }

        return null;
    }

    private static Dictionary<string, string> CreatePlacements(
        ProjectLockFile.PackInstance instance,
        IReadOnlyList<ProjectLockFile.ManagedFile> managedFiles
    )
    {
        var placements = new Dictionary<string, string>(
            instance.Placements,
            StringComparer.Ordinal
        );
        foreach (var file in managedFiles)
        {
            if (file.DeclaredTargetPath is { } declaredTarget)
            {
                placements[declaredTarget] = file.TargetPath;
            }
        }

        return placements;
    }

    private static string? TryCreateInstances(
        ProjectConfiguration configuration,
        ProjectLockFile lockFile,
        out List<ProjectLockFile.PackInstance> instances
    )
    {
        instances = new List<ProjectLockFile.PackInstance>(configuration.Packs.Count);
        var requestedIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var requestedPack in configuration.Packs)
        {
            if (
                !requestedIds.Add(requestedPack.Id)
                || requestedPack.Name is { } name
                    && !string.Equals(name, requestedPack.Id, StringComparison.Ordinal)
            )
            {
                return $"Legacy lock state cannot unambiguously correlate requested pack '{requestedPack.Id}'.";
            }

            var matches = lockFile
                .Packs.Where(pack =>
                    string.Equals(pack.Id, requestedPack.Id, StringComparison.Ordinal)
                    && (
                        requestedPack.Version is null
                        || string.Equals(
                            pack.Version,
                            requestedPack.Version,
                            StringComparison.Ordinal
                        )
                    )
                )
                .ToList();
            if (matches.Count != 1 || matches[0].Key is not { } rootResolution)
            {
                return $"Legacy lock state cannot unambiguously correlate requested pack '{requestedPack.Id}'.";
            }

            var root = matches[0];
            var placements = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var managedFile in root.ManagedFiles)
            {
                if (
                    managedFile.DeclaredTargetPath is not { } declaredTarget
                    || !placements.TryAdd(declaredTarget, managedFile.TargetPath)
                )
                {
                    return $"Legacy lock state contains ambiguous placement evidence for requested pack '{requestedPack.Id}'.";
                }
            }

            instances.Add(
                new ProjectLockFile.PackInstance
                {
                    Destination = requestedPack.Destination,
                    ExternalSources = root.ExternalSources,
                    Id = requestedPack.Id,
                    ManagedFiles = root.ManagedFiles,
                    Name = requestedPack.Id,
                    Placements = placements,
                    RootResolution = rootResolution,
                }
            );
            root.Destination = null;
            root.ExternalSources = [];
            root.ManagedFiles = [];
        }

        return null;
    }

    private static ProjectLockFile.ResolvedPackKey CreateKey(ProjectLockFile.ResolvedPack pack) =>
        new()
        {
            Id = pack.Id,
            ResolvedCommit = pack.GitSource?.ResolvedCommit,
            SourceIdentity = pack.SourceIdentity!,
            Version = pack.Version,
        };

    private static ManifestOperationResult<ProjectLockFile> Failure(string error) =>
        ManifestOperationResult<ProjectLockFile>.Failure(error);
}
