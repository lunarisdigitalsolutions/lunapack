using Lunapack.Cli.Packs.ManagedFiles;
using Lunapack.Cli.Project;

namespace Lunapack.Cli.Audit;

internal static class AuditOutputFormatter
{
    public static string Format(ProjectLockFile.ResolvedPack pack)
    {
        var provenance = $"{pack.SourcePath}/{pack.PackPath}";
        var dependencies = string.Join(", ", pack.Packs.Select(FormatReference));
        var managedTargets = string.Join(", ", pack.ManagedFiles.Select(FormatManagedFile));

        var output = $"{pack.Id}@{pack.Version}{Environment.NewLine}  source: {provenance}";
        if (!string.IsNullOrEmpty(dependencies))
        {
            output += $"{Environment.NewLine}  depends on: {dependencies}";
        }

        return string.IsNullOrEmpty(managedTargets)
            ? output
            : $"{output}{Environment.NewLine}  manages: {managedTargets}";
    }

    private static string FormatReference(ProjectLockFile.PackReference pack) =>
        $"{pack.Id}@{pack.Version}";

    private static string FormatManagedFile(ProjectLockFile.ManagedFile file) =>
        $"{file.TargetPath} ({file.Sha256})";

    public static string Format(AuditReport.ExternalSource source) =>
        $"external source: {source.PackId}@{source.PackVersion} alias={source.Alias} workspace={source.WorkspaceSourceName} fingerprint={source.Fingerprint} ref={source.Ref} commit={source.ResolvedCommit} status={source.Status}";

    public static string Format(AuditReport.ExternalFile file) =>
        $"external file: {file.PackId}@{file.PackVersion} alias={file.Alias} workspace={file.WorkspaceSourceName} fingerprint={file.Fingerprint} ref={file.Ref} commit={file.ResolvedCommit} source={file.SourcePath} target={file.TargetPath} sha256={file.Sha256} status={file.Status}";

    public static string Format(ManagedRootOwner owner) =>
        owner.Kind switch
        {
            ManagedRootKind.Pack => owner.Version is null
                ? $"pack '{owner.Name}'"
                : $"pack '{owner.Name}@{owner.Version}'",
            ManagedRootKind.Link => $"link '{owner.Name}'",
            _ => throw new ArgumentOutOfRangeException(nameof(owner)),
        };

    public static string Format(ManagedFileIntegrityStatus status) =>
        status switch
        {
            ManagedFileIntegrityStatus.Current => "current",
            ManagedFileIntegrityStatus.Missing => "missing",
            ManagedFileIntegrityStatus.Modified => "modified",
            _ => throw new ArgumentOutOfRangeException(nameof(status)),
        };
}
