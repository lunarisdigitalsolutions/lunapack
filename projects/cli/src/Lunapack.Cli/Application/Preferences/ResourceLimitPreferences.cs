namespace Lunapack.Cli.Application.Preferences;

internal sealed record ResourceLimitPreferences
{
    public long? ProjectManifestBytes { get; set; }

    public long? PackManifestBytes { get; set; }

    public long? LockFileBytes { get; set; }

    public long? PackDocumentBytes { get; set; }

    public long? PackSnapshotBytes { get; set; }

    public long? RenderedFileBytes { get; set; }

    public long? RenderedTotalBytes { get; set; }

    public long? DependencyDepth { get; set; }

    public long? DependencyNodes { get; set; }

    public long? DependencyReferences { get; set; }

    public long? FilesPerPack { get; set; }

    public long? FilesPerGraph { get; set; }

    public long? OciArchiveEntries { get; set; }

    public long? OciDownloadBytes { get; set; }

    public long? OciEntryBytes { get; set; }

    public long? OciExpandedBytes { get; set; }

    public long? GetValue(ResourceLimits.ResourceLimitKey key) =>
        key switch
        {
            ResourceLimits.ResourceLimitKey.ProjectManifestBytes => ProjectManifestBytes,
            ResourceLimits.ResourceLimitKey.PackManifestBytes => PackManifestBytes,
            ResourceLimits.ResourceLimitKey.LockFileBytes => LockFileBytes,
            ResourceLimits.ResourceLimitKey.PackDocumentBytes => PackDocumentBytes,
            ResourceLimits.ResourceLimitKey.PackSnapshotBytes => PackSnapshotBytes,
            ResourceLimits.ResourceLimitKey.RenderedFileBytes => RenderedFileBytes,
            ResourceLimits.ResourceLimitKey.RenderedTotalBytes => RenderedTotalBytes,
            ResourceLimits.ResourceLimitKey.DependencyDepth => DependencyDepth,
            ResourceLimits.ResourceLimitKey.DependencyNodes => DependencyNodes,
            ResourceLimits.ResourceLimitKey.DependencyReferences => DependencyReferences,
            ResourceLimits.ResourceLimitKey.FilesPerPack => FilesPerPack,
            ResourceLimits.ResourceLimitKey.FilesPerGraph => FilesPerGraph,
            ResourceLimits.ResourceLimitKey.OciArchiveEntries => OciArchiveEntries,
            ResourceLimits.ResourceLimitKey.OciDownloadBytes => OciDownloadBytes,
            ResourceLimits.ResourceLimitKey.OciEntryBytes => OciEntryBytes,
            ResourceLimits.ResourceLimitKey.OciExpandedBytes => OciExpandedBytes,
            _ => throw new ArgumentOutOfRangeException(nameof(key), key, null),
        };

    public ResourceLimitPreferences? WithValue(ResourceLimits.ResourceLimitKey key, long? value)
    {
        var updated = key switch
        {
            ResourceLimits.ResourceLimitKey.ProjectManifestBytes => this with
            {
                ProjectManifestBytes = value,
            },
            ResourceLimits.ResourceLimitKey.PackManifestBytes => this with
            {
                PackManifestBytes = value,
            },
            ResourceLimits.ResourceLimitKey.LockFileBytes => this with { LockFileBytes = value },
            ResourceLimits.ResourceLimitKey.PackDocumentBytes => this with
            {
                PackDocumentBytes = value,
            },
            ResourceLimits.ResourceLimitKey.PackSnapshotBytes => this with
            {
                PackSnapshotBytes = value,
            },
            ResourceLimits.ResourceLimitKey.RenderedFileBytes => this with
            {
                RenderedFileBytes = value,
            },
            ResourceLimits.ResourceLimitKey.RenderedTotalBytes => this with
            {
                RenderedTotalBytes = value,
            },
            ResourceLimits.ResourceLimitKey.DependencyDepth => this with
            {
                DependencyDepth = value,
            },
            ResourceLimits.ResourceLimitKey.DependencyNodes => this with
            {
                DependencyNodes = value,
            },
            ResourceLimits.ResourceLimitKey.DependencyReferences => this with
            {
                DependencyReferences = value,
            },
            ResourceLimits.ResourceLimitKey.FilesPerPack => this with { FilesPerPack = value },
            ResourceLimits.ResourceLimitKey.FilesPerGraph => this with { FilesPerGraph = value },
            ResourceLimits.ResourceLimitKey.OciArchiveEntries => this with
            {
                OciArchiveEntries = value,
            },
            ResourceLimits.ResourceLimitKey.OciDownloadBytes => this with
            {
                OciDownloadBytes = value,
            },
            ResourceLimits.ResourceLimitKey.OciEntryBytes => this with { OciEntryBytes = value },
            ResourceLimits.ResourceLimitKey.OciExpandedBytes => this with
            {
                OciExpandedBytes = value,
            },
            _ => throw new ArgumentOutOfRangeException(nameof(key), key, null),
        };

        return updated.HasValues() ? updated : null;
    }

    public bool HasValues() =>
        ProjectManifestBytes is not null
        || PackManifestBytes is not null
        || LockFileBytes is not null
        || PackDocumentBytes is not null
        || PackSnapshotBytes is not null
        || RenderedFileBytes is not null
        || RenderedTotalBytes is not null
        || DependencyDepth is not null
        || DependencyNodes is not null
        || DependencyReferences is not null
        || FilesPerPack is not null
        || FilesPerGraph is not null
        || OciArchiveEntries is not null
        || OciDownloadBytes is not null
        || OciEntryBytes is not null
        || OciExpandedBytes is not null;
}
