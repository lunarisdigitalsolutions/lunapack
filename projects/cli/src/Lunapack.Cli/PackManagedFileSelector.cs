namespace Lunapack.Cli;

internal sealed record PackManagedFileSelector(
    PackManagedFileSelectorKind Kind,
    string Value,
    string? SourceAlias,
    IReadOnlyList<string> Exclusions,
    bool Flatten
)
{
    public bool IsExternal => SourceAlias is not null;

    private static List<(PackManagedFileSelectorKind Kind, string Value)> GetDeclaredSelectors(
        PackManifest.PackManagedFile managedFile
    )
    {
        var declared = new List<(PackManagedFileSelectorKind Kind, string Value)>(3);
        if (!string.IsNullOrEmpty(managedFile.Path))
        {
            declared.Add((PackManagedFileSelectorKind.File, managedFile.Path));
        }

        if (!string.IsNullOrEmpty(managedFile.Directory))
        {
            declared.Add((PackManagedFileSelectorKind.Directory, managedFile.Directory));
        }

        if (!string.IsNullOrEmpty(managedFile.Glob))
        {
            declared.Add((PackManagedFileSelectorKind.Glob, managedFile.Glob));
        }

        return declared;
    }

    public static ManifestOperationResult<PackManagedFileSelector> Create(
        PackManifest.PackManagedFile managedFile
    )
    {
        var declared = GetDeclaredSelectors(managedFile);
        if (declared.Count > 1)
        {
            return ManifestOperationResult<PackManagedFileSelector>.Failure(
                $"Managed file '{managedFile.Target}' must declare exactly one of 'path', 'directory', or 'glob'."
            );
        }

        var alias = string.IsNullOrEmpty(managedFile.Source) ? null : managedFile.Source;
        if (declared.Count == 0)
        {
            return alias is null
                ? ManifestOperationResult<PackManagedFileSelector>.Failure(
                    $"Managed file '{managedFile.Target}' has no selector."
                )
                : ManifestOperationResult<PackManagedFileSelector>.Success(
                    new PackManagedFileSelector(
                        PackManagedFileSelectorKind.File,
                        alias,
                        SourceAlias: null,
                        Exclusions: [],
                        Flatten: false
                    )
                );
        }

        var (kind, value) = declared[0];
        if (kind == PackManagedFileSelectorKind.File && managedFile.Flatten)
        {
            return ManifestOperationResult<PackManagedFileSelector>.Failure(
                $"Managed file '{managedFile.Target}' cannot flatten a single-file selector."
            );
        }

        if (kind == PackManagedFileSelectorKind.File && managedFile.Exclude.Count > 0)
        {
            return ManifestOperationResult<PackManagedFileSelector>.Failure(
                $"Managed file '{managedFile.Target}' cannot exclude paths from a single-file selector."
            );
        }

        return ManifestOperationResult<PackManagedFileSelector>.Success(
            new PackManagedFileSelector(
                kind,
                value,
                alias,
                [.. managedFile.Exclude],
                managedFile.Flatten
            )
        );
    }
}
