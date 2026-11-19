using System.IO.Abstractions;
using System.Security.Cryptography;
using Lunapack.Cli.Application.CommandExecution;
using Lunapack.Cli.Application.Paths;
using Lunapack.Cli.Packs.ManagedFiles;
using Lunapack.Cli.Project;
using Lunapack.Cli.Sources;

namespace Lunapack.Cli.Audit;

internal sealed class AuditService(IFileSystem fileSystem, ProjectStateStore projectStateStore)
{
    public async Task<ManifestOperationResult<AuditReport>> InspectAsync(string projectDirectory)
    {
        var loaded = await projectStateStore.LoadAsync(projectDirectory);
        if (loaded.Value is not { } state)
        {
            return ManifestOperationResult<AuditReport>.Failure(
                loaded.Error ?? "Unable to load project state."
            );
        }

        var sources = new List<AuditReport.ExternalSource>();
        var files = new List<AuditReport.ExternalFile>();
        foreach (var pack in state.LockFile.Packs.OrderBy(pack => pack.Id, StringComparer.Ordinal))
        {
            foreach (var (alias, source) in pack.ExternalSources)
            {
                sources.Add(
                    new AuditReport.ExternalSource(
                        pack.Id,
                        pack.Version,
                        alias,
                        source.SourceName,
                        source.Fingerprint,
                        source.Ref,
                        source.ResolvedCommit,
                        GetSourceStatus(state.Configuration, source)
                    )
                );
            }

            foreach (var managedFile in pack.ManagedFiles)
            {
                var externalFile = CreateExternalFile(projectDirectory, pack, managedFile);
                if (externalFile.Error is { } error)
                {
                    return ManifestOperationResult<AuditReport>.Failure(error);
                }

                if (externalFile.File is { } file)
                {
                    files.Add(file);
                }
            }
        }

        return ManifestOperationResult<AuditReport>.Success(
            new AuditReport(state.LockFile.Packs, sources, files)
        );
    }

    public ManifestOperationResult<IReadOnlyList<ManagedFileIntegrityResult>> InspectManagedFiles(
        string projectDirectory,
        ProjectLockFile lockFile,
        bool failFast
    )
    {
        ArgumentNullException.ThrowIfNull(lockFile);

        var reports = new List<ManagedFileIntegrityResult>();
        var roots = ManagedRootInventory
            .FromLockFile(lockFile)
            .OrderBy(root => root.Owner.Kind)
            .ThenBy(root => root.Owner.Name, StringComparer.Ordinal)
            .ThenBy(root => root.Owner.Version, StringComparer.Ordinal);
        foreach (var root in roots)
        {
            foreach (
                var file in root.Files.OrderBy(file => file.TargetPath, StringComparer.Ordinal)
            )
            {
                var inspected = InspectManagedFile(projectDirectory, root.Owner, file);
                if (inspected.Value is not { } report)
                {
                    return ManifestOperationResult<
                        IReadOnlyList<ManagedFileIntegrityResult>
                    >.Failure(inspected.Error ?? "Unable to inspect managed file.");
                }

                reports.Add(report);
                if (failFast && report.IsViolation)
                {
                    return ManifestOperationResult<
                        IReadOnlyList<ManagedFileIntegrityResult>
                    >.Success(reports);
                }
            }
        }

        return ManifestOperationResult<IReadOnlyList<ManagedFileIntegrityResult>>.Success(reports);
    }

    private ManifestOperationResult<ManagedFileIntegrityResult> InspectManagedFile(
        string projectDirectory,
        ManagedRootOwner owner,
        ManagedRootFile file
    )
    {
        try
        {
            using var stream = ProjectFileReader.OpenRead(
                fileSystem,
                projectDirectory,
                file.TargetPath
            );
            var digest = Convert.ToHexString(SHA256.HashData(stream));
            var status = string.Equals(digest, file.Sha256, StringComparison.OrdinalIgnoreCase)
                ? ManagedFileIntegrityStatus.Current
                : ManagedFileIntegrityStatus.Modified;
            return ManifestOperationResult<ManagedFileIntegrityResult>.Success(
                new ManagedFileIntegrityResult(owner, file.TargetPath, status)
            );
        }
        catch (Exception exception)
            when (exception is FileNotFoundException or DirectoryNotFoundException)
        {
            return ManifestOperationResult<ManagedFileIntegrityResult>.Success(
                new ManagedFileIntegrityResult(
                    owner,
                    file.TargetPath,
                    ManagedFileIntegrityStatus.Missing
                )
            );
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return ManifestOperationResult<ManagedFileIntegrityResult>.Failure(
                $"Unable to read managed target '{file.TargetPath}' owned by {AuditOutputFormatter.Format(owner)}: {exception.Message}"
            );
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
        {
            return ManifestOperationResult<ManagedFileIntegrityResult>.Failure(
                $"Unable to resolve managed target '{file.TargetPath}' owned by {AuditOutputFormatter.Format(owner)}: {exception.Message}"
            );
        }
    }

    private (AuditReport.ExternalFile? File, string? Error) CreateExternalFile(
        string projectDirectory,
        ProjectLockFile.ResolvedPack pack,
        ProjectLockFile.ManagedFile managedFile
    )
    {
        if (
            managedFile is
            { SourceAlias: null, SourceName: null, SourceFingerprint: null, SourcePath: null }
        )
        {
            return (null, null);
        }

        if (
            managedFile
                is not {
                    SourceAlias: { } alias,
                    SourceName: { } sourceName,
                    SourceFingerprint: { } sourceFingerprint,
                    SourcePath: { } sourcePath,
                }
            || !pack.ExternalSources.TryGetValue(alias, out var source)
            || source is null
        )
        {
            return (
                null,
                $"Invalid external source metadata for managed file '{managedFile.TargetPath}' in pack '{pack.Id}@{pack.Version}'."
            );
        }

        var target = GetTargetStatus(projectDirectory, managedFile);
        if (target.Value is not { } targetStatus)
        {
            return (null, target.Error ?? "Unable to inspect managed target.");
        }

        return (
            new AuditReport.ExternalFile(
                pack.Id,
                pack.Version,
                alias,
                sourceName,
                sourceFingerprint,
                source.Ref,
                source.ResolvedCommit,
                sourcePath,
                managedFile.TargetPath,
                managedFile.Sha256,
                targetStatus
            ),
            null
        );
    }

    private static string GetSourceStatus(
        ProjectConfiguration configuration,
        ProjectLockFile.ExternalSourceLock lockedSource
    )
    {
        var configured = configuration.Sources.SingleOrDefault(source =>
            string.Equals(source.Name, lockedSource.SourceName, StringComparison.Ordinal)
        );
        if (configured is null)
        {
            return "missing workspace source";
        }

        var fingerprint = SourceIdentityNormalizer.Create(configured);
        return fingerprint.Value is not { } configuredFingerprint ? "invalid workspace source"
            : string.Equals(
                configuredFingerprint.Value,
                lockedSource.Fingerprint,
                StringComparison.Ordinal
            )
                ? "current"
            : "configuration drift";
    }

    private ManifestOperationResult<string> GetTargetStatus(
        string projectDirectory,
        ProjectLockFile.ManagedFile managedFile
    )
    {
        try
        {
            using var stream = ProjectFileReader.OpenRead(
                fileSystem,
                projectDirectory,
                managedFile.TargetPath
            );
            var digest = Convert.ToHexString(SHA256.HashData(stream));
            return ManifestOperationResult<string>.Success(
                string.Equals(digest, managedFile.Sha256, StringComparison.OrdinalIgnoreCase)
                    ? "current"
                    : "locally modified"
            );
        }
        catch (Exception exception)
            when (exception is FileNotFoundException or DirectoryNotFoundException)
        {
            return ManifestOperationResult<string>.Success("missing target");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return ManifestOperationResult<string>.Failure(
                $"Unable to read managed target '{managedFile.TargetPath}': {exception.Message}"
            );
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
        {
            return ManifestOperationResult<string>.Failure(
                $"Unable to resolve managed target '{managedFile.TargetPath}': {exception.Message}"
            );
        }
    }
}
