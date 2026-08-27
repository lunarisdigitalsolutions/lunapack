using System.IO.Abstractions;
using System.Security.Cryptography;

namespace Lunapack.Cli;

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

            foreach (
                var managedFile in pack.ManagedFiles.Where(file => file.SourceAlias is not null)
            )
            {
                var source = pack.ExternalSources[managedFile.SourceAlias!];
                files.Add(
                    new AuditReport.ExternalFile(
                        pack.Id,
                        pack.Version,
                        managedFile.SourceAlias!,
                        managedFile.SourceName!,
                        managedFile.SourceFingerprint!,
                        source.Ref,
                        source.ResolvedCommit,
                        managedFile.SourcePath!,
                        managedFile.TargetPath,
                        managedFile.Sha256,
                        GetTargetStatus(projectDirectory, managedFile)
                    )
                );
            }
        }

        return ManifestOperationResult<AuditReport>.Success(
            new AuditReport(state.LockFile.Packs, sources, files)
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
