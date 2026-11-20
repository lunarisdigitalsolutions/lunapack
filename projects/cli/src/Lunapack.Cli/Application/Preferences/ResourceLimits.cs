namespace Lunapack.Cli.Application.Preferences;

internal sealed record ResourceLimits(
    long ProjectManifestBytes,
    long PackManifestBytes,
    long LockFileBytes,
    long PackDocumentBytes,
    long PackSnapshotBytes,
    long RenderedFileBytes,
    long RenderedTotalBytes,
    long DependencyDepth,
    long DependencyNodes,
    long DependencyReferences,
    long FilesPerPack,
    long FilesPerGraph,
    long OciArchiveEntries,
    long OciDownloadBytes,
    long OciEntryBytes,
    long OciExpandedBytes
)
{
    private const long Mebibyte = 1024 * 1024;
    private const long Gibibyte = 1024 * 1024 * 1024;

    private static readonly Definition[] _definitions =
    [
        new(
            ResourceLimitKey.ProjectManifestBytes,
            "resource-limits.project-manifest-bytes",
            Mebibyte,
            16 * Mebibyte,
            "bytes"
        ),
        new(
            ResourceLimitKey.PackManifestBytes,
            "resource-limits.pack-manifest-bytes",
            Mebibyte,
            64 * Mebibyte,
            "bytes"
        ),
        new(
            ResourceLimitKey.LockFileBytes,
            "resource-limits.lock-file-bytes",
            64 * Mebibyte,
            Gibibyte,
            "bytes"
        ),
        new(
            ResourceLimitKey.PackDocumentBytes,
            "resource-limits.pack-document-bytes",
            16 * Mebibyte,
            256 * Mebibyte,
            "bytes"
        ),
        new(
            ResourceLimitKey.PackSnapshotBytes,
            "resource-limits.pack-snapshot-bytes",
            2 * Gibibyte,
            16 * Gibibyte,
            "bytes"
        ),
        new(
            ResourceLimitKey.RenderedFileBytes,
            "resource-limits.rendered-file-bytes",
            64 * Mebibyte,
            256 * Mebibyte,
            "bytes"
        ),
        new(
            ResourceLimitKey.RenderedTotalBytes,
            "resource-limits.rendered-total-bytes",
            512 * Mebibyte,
            4 * Gibibyte,
            "bytes"
        ),
        new(ResourceLimitKey.DependencyDepth, "resource-limits.dependency-depth", 64, 128, "edges"),
        new(
            ResourceLimitKey.DependencyNodes,
            "resource-limits.dependency-nodes",
            1_024,
            100_000,
            "nodes"
        ),
        new(
            ResourceLimitKey.DependencyReferences,
            "resource-limits.dependency-references",
            100_000,
            1_000_000,
            "references"
        ),
        new(
            ResourceLimitKey.FilesPerPack,
            "resource-limits.files-per-pack",
            10_000,
            100_000,
            "files"
        ),
        new(
            ResourceLimitKey.FilesPerGraph,
            "resource-limits.files-per-graph",
            100_000,
            1_000_000,
            "files"
        ),
        new(
            ResourceLimitKey.OciArchiveEntries,
            "resource-limits.oci-archive-entries",
            100_000,
            1_000_000,
            "entries"
        ),
        new(
            ResourceLimitKey.OciDownloadBytes,
            "resource-limits.oci-download-bytes",
            512 * Mebibyte,
            16 * Gibibyte,
            "bytes"
        ),
        new(
            ResourceLimitKey.OciEntryBytes,
            "resource-limits.oci-entry-bytes",
            512 * Mebibyte,
            4 * Gibibyte,
            "bytes"
        ),
        new(
            ResourceLimitKey.OciExpandedBytes,
            "resource-limits.oci-expanded-bytes",
            2 * Gibibyte,
            16 * Gibibyte,
            "bytes"
        ),
    ];

    private static readonly Dictionary<string, ResourceLimitKey> _yamlKeys =
        _definitions.ToDictionary(
            definition =>
                string.Concat(
                    definition
                        .Name["resource-limits.".Length..]
                        .Split('-')
                        .Select(
                            (segment, index) =>
                                index == 0
                                    ? segment
                                    : char.ToUpperInvariant(segment[0]) + segment[1..]
                        )
                ),
            definition => definition.Key,
            StringComparer.Ordinal
        );

    public static ResourceLimits Default { get; } =
        new(
            ProjectManifestBytes: 1 * Mebibyte,
            PackManifestBytes: 1 * Mebibyte,
            LockFileBytes: 64 * Mebibyte,
            PackDocumentBytes: 16 * Mebibyte,
            PackSnapshotBytes: 2 * Gibibyte,
            RenderedFileBytes: 64 * Mebibyte,
            RenderedTotalBytes: 512 * Mebibyte,
            DependencyDepth: 64,
            DependencyNodes: 1_024,
            DependencyReferences: 100_000,
            FilesPerPack: 10_000,
            FilesPerGraph: 100_000,
            OciArchiveEntries: 100_000,
            OciDownloadBytes: 512 * Mebibyte,
            OciEntryBytes: 512 * Mebibyte,
            OciExpandedBytes: 2 * Gibibyte
        );

    public static long ProjectSettingsBootstrapBytes { get; } = 16 * Mebibyte;

    public static long UserSettingsBootstrapBytes { get; } = 1 * Mebibyte;

    public static IReadOnlyList<Definition> Definitions => _definitions;

    public static ResourceLimits Resolve(
        ResourceLimitPreferences? local,
        ResourceLimitPreferences? userProject,
        ResourceLimitPreferences? global
    ) => Resolve(local, ResolveUserValues(userProject, global));

    public static ResourceLimits Resolve(
        ResourceLimitPreferences? local,
        ResourceLimits userBaseline
    ) =>
        new(
            ResolveLocalValue(local, userBaseline, ResourceLimitKey.ProjectManifestBytes),
            ResolveLocalValue(local, userBaseline, ResourceLimitKey.PackManifestBytes),
            ResolveLocalValue(local, userBaseline, ResourceLimitKey.LockFileBytes),
            ResolveLocalValue(local, userBaseline, ResourceLimitKey.PackDocumentBytes),
            ResolveLocalValue(local, userBaseline, ResourceLimitKey.PackSnapshotBytes),
            ResolveLocalValue(local, userBaseline, ResourceLimitKey.RenderedFileBytes),
            ResolveLocalValue(local, userBaseline, ResourceLimitKey.RenderedTotalBytes),
            ResolveLocalValue(local, userBaseline, ResourceLimitKey.DependencyDepth),
            ResolveLocalValue(local, userBaseline, ResourceLimitKey.DependencyNodes),
            ResolveLocalValue(local, userBaseline, ResourceLimitKey.DependencyReferences),
            ResolveLocalValue(local, userBaseline, ResourceLimitKey.FilesPerPack),
            ResolveLocalValue(local, userBaseline, ResourceLimitKey.FilesPerGraph),
            ResolveLocalValue(local, userBaseline, ResourceLimitKey.OciArchiveEntries),
            ResolveLocalValue(local, userBaseline, ResourceLimitKey.OciDownloadBytes),
            ResolveLocalValue(local, userBaseline, ResourceLimitKey.OciEntryBytes),
            ResolveLocalValue(local, userBaseline, ResourceLimitKey.OciExpandedBytes)
        );

    public static bool TryParseKey(string key, out ResourceLimitKey resourceLimitKey)
    {
        var definition = _definitions.FirstOrDefault(candidate =>
            string.Equals(candidate.Name, key, StringComparison.Ordinal)
        );
        if (definition is null)
        {
            resourceLimitKey = default;
            return false;
        }

        resourceLimitKey = definition.Key;
        return true;
    }

    public static bool TryParseYamlKey(string key, out ResourceLimitKey resourceLimitKey) =>
        _yamlKeys.TryGetValue(key, out resourceLimitKey);

    public static Definition GetDefinition(ResourceLimitKey key) =>
        _definitions.First(definition => definition.Key == key);

    public static IReadOnlyList<string> ValidateOverrides(
        ResourceLimitPreferences? preferences,
        bool projectScope
    )
    {
        var issues = new List<string>();
        if (preferences is null)
        {
            return issues;
        }

        foreach (var definition in _definitions)
        {
            if (preferences.GetValue(definition.Key) is not { } value)
            {
                continue;
            }

            if (value <= 0 || value > definition.MaximumValue)
            {
                issues.Add(
                    $"Resource limit '{definition.Name}' must be a positive integer no greater than {definition.MaximumValue} {definition.Unit}."
                );
            }
            else if (projectScope && value > definition.DefaultValue)
            {
                issues.Add(
                    $"Project resource limit '{definition.Name}' cannot exceed the built-in default of {definition.DefaultValue} {definition.Unit}."
                );
            }
        }

        return issues;
    }

    public long GetValue(ResourceLimitKey key) =>
        key switch
        {
            ResourceLimitKey.ProjectManifestBytes => ProjectManifestBytes,
            ResourceLimitKey.PackManifestBytes => PackManifestBytes,
            ResourceLimitKey.LockFileBytes => LockFileBytes,
            ResourceLimitKey.PackDocumentBytes => PackDocumentBytes,
            ResourceLimitKey.PackSnapshotBytes => PackSnapshotBytes,
            ResourceLimitKey.RenderedFileBytes => RenderedFileBytes,
            ResourceLimitKey.RenderedTotalBytes => RenderedTotalBytes,
            ResourceLimitKey.DependencyDepth => DependencyDepth,
            ResourceLimitKey.DependencyNodes => DependencyNodes,
            ResourceLimitKey.DependencyReferences => DependencyReferences,
            ResourceLimitKey.FilesPerPack => FilesPerPack,
            ResourceLimitKey.FilesPerGraph => FilesPerGraph,
            ResourceLimitKey.OciArchiveEntries => OciArchiveEntries,
            ResourceLimitKey.OciDownloadBytes => OciDownloadBytes,
            ResourceLimitKey.OciEntryBytes => OciEntryBytes,
            ResourceLimitKey.OciExpandedBytes => OciExpandedBytes,
            _ => throw new ArgumentOutOfRangeException(nameof(key), key, null),
        };

    private static ResourceLimits ResolveUserValues(
        ResourceLimitPreferences? userProject,
        ResourceLimitPreferences? global
    ) =>
        new(
            ResolveUserValue(userProject, global, ResourceLimitKey.ProjectManifestBytes),
            ResolveUserValue(userProject, global, ResourceLimitKey.PackManifestBytes),
            ResolveUserValue(userProject, global, ResourceLimitKey.LockFileBytes),
            ResolveUserValue(userProject, global, ResourceLimitKey.PackDocumentBytes),
            ResolveUserValue(userProject, global, ResourceLimitKey.PackSnapshotBytes),
            ResolveUserValue(userProject, global, ResourceLimitKey.RenderedFileBytes),
            ResolveUserValue(userProject, global, ResourceLimitKey.RenderedTotalBytes),
            ResolveUserValue(userProject, global, ResourceLimitKey.DependencyDepth),
            ResolveUserValue(userProject, global, ResourceLimitKey.DependencyNodes),
            ResolveUserValue(userProject, global, ResourceLimitKey.DependencyReferences),
            ResolveUserValue(userProject, global, ResourceLimitKey.FilesPerPack),
            ResolveUserValue(userProject, global, ResourceLimitKey.FilesPerGraph),
            ResolveUserValue(userProject, global, ResourceLimitKey.OciArchiveEntries),
            ResolveUserValue(userProject, global, ResourceLimitKey.OciDownloadBytes),
            ResolveUserValue(userProject, global, ResourceLimitKey.OciEntryBytes),
            ResolveUserValue(userProject, global, ResourceLimitKey.OciExpandedBytes)
        );

    private static long ResolveUserValue(
        ResourceLimitPreferences? userProject,
        ResourceLimitPreferences? global,
        ResourceLimitKey key
    ) => userProject?.GetValue(key) ?? global?.GetValue(key) ?? GetDefinition(key).DefaultValue;

    private static long ResolveLocalValue(
        ResourceLimitPreferences? local,
        ResourceLimits userBaseline,
        ResourceLimitKey key
    )
    {
        var baseline = userBaseline.GetValue(key);
        return local?.GetValue(key) is { } localValue ? Math.Min(localValue, baseline) : baseline;
    }

    internal enum ResourceLimitKey
    {
        ProjectManifestBytes,
        PackManifestBytes,
        LockFileBytes,
        PackDocumentBytes,
        PackSnapshotBytes,
        RenderedFileBytes,
        RenderedTotalBytes,
        DependencyDepth,
        DependencyNodes,
        DependencyReferences,
        FilesPerPack,
        FilesPerGraph,
        OciArchiveEntries,
        OciDownloadBytes,
        OciEntryBytes,
        OciExpandedBytes,
    }

    internal sealed record Definition(
        ResourceLimitKey Key,
        string Name,
        long DefaultValue,
        long MaximumValue,
        string Unit
    );
}
