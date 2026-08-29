using System.IO.Abstractions;
using System.Security.Cryptography;
using Lunapack.Cli.Application.CommandExecution;
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
                GetTargetStatus(projectDirectory, managedFile)
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

    private string GetTargetStatus(string projectDirectory, ProjectLockFile.ManagedFile managedFile)
    {
        var targetPath = fileSystem.Path.GetFullPath(managedFile.TargetPath, projectDirectory);
        if (!fileSystem.File.Exists(targetPath))
        {
            return "missing target";
        }

        var digest = Convert.ToHexString(SHA256.HashData(fileSystem.File.ReadAllBytes(targetPath)));
        return string.Equals(digest, managedFile.Sha256, StringComparison.OrdinalIgnoreCase)
            ? "current"
            : "locally modified";
    }
}
