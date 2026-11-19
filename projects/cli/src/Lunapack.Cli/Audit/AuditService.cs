using System.IO.Abstractions;
using System.Security.Cryptography;
using Lunapack.Cli.Application.CommandExecution;
using Lunapack.Cli.Packs.ContentScreening;
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
        var hiddenCharacterFindings = new List<PackContentFinding>();
        var omittedHiddenCharacterFindingCount = 0L;
        var screenedTargets = new HashSet<string>(StringComparer.Ordinal);
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
                if (screenedTargets.Add(managedFile.TargetPath))
                {
                    var screening = ScreenManagedFile(projectDirectory, managedFile);
                    if (screening.Value is not { } result)
                    {
                        return ManifestOperationResult<AuditReport>.Failure(
                            screening.Error
                                ?? $"Unable to screen managed target '{PackContentDiagnosticFormatter.EscapePath(managedFile.TargetPath)}'."
                        );
                    }

                    hiddenCharacterFindings.AddRange(result.Findings);
                    omittedHiddenCharacterFindingCount += result.OmittedFindingCount;
                }

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
            new AuditReport(
                state.LockFile.Packs,
                sources,
                files,
                hiddenCharacterFindings,
                omittedHiddenCharacterFindingCount
            )
        );
    }

    private ManifestOperationResult<PackContentScanResult> ScreenManagedFile(
        string projectDirectory,
        ProjectLockFile.ManagedFile managedFile
    )
    {
        var targetPath = fileSystem.Path.GetFullPath(managedFile.TargetPath, projectDirectory);
        if (!fileSystem.File.Exists(targetPath))
        {
            return ManifestOperationResult<PackContentScanResult>.Success(
                new PackContentScanResult([], 0, false)
            );
        }

        try
        {
            return ManifestOperationResult<PackContentScanResult>.Success(
                PackContentScanner.Scan(
                    managedFile.TargetPath,
                    fileSystem.File.ReadAllBytes(targetPath)
                )
            );
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return ManifestOperationResult<PackContentScanResult>.Failure(
                $"Unable to read managed target '{PackContentDiagnosticFormatter.EscapePath(managedFile.TargetPath)}' during audit: {PackContentDiagnosticFormatter.EscapePath(exception.Message)}"
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
