namespace Lunapack.Cli;

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
}
