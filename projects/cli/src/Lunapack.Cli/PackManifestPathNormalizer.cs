namespace Lunapack.Cli;

internal static class PackManifestPathNormalizer
{
    public static PackManifest Normalize(PackManifest manifest) =>
        manifest with
        {
            ManagedFiles =
            [
                .. manifest.ManagedFiles.Select(managedFile =>
                    managedFile with
                    {
                        Directory = ProjectPath.NormalizeOptional(managedFile.Directory),
                        Exclude = [.. managedFile.Exclude.Select(ProjectPath.Normalize)],
                        Glob = ProjectPath.NormalizeOptional(managedFile.Glob),
                        Path = ProjectPath.NormalizeOptional(managedFile.Path),
                        Source = NormalizeSource(managedFile),
                        Target = ProjectPath.Normalize(managedFile.Target),
                    }
                ),
            ],
            Hooks = NormalizeHooks(manifest.Hooks),
            Sources = manifest.Sources.ToDictionary(
                source => source.Key,
                source =>
                    source.Value with
                    {
                        Path = ProjectPath.NormalizeOptional(source.Value.Path),
                    },
                StringComparer.Ordinal
            ),
        };

    private static string? NormalizeSource(PackManifest.PackManagedFile managedFile) =>
        IsExternalAlias(managedFile)
            ? managedFile.Source
            : ProjectPath.NormalizeOptional(managedFile.Source);

    private static bool IsExternalAlias(PackManifest.PackManagedFile managedFile) =>
        !string.IsNullOrEmpty(managedFile.Source)
        && (
            !string.IsNullOrEmpty(managedFile.Path)
            || !string.IsNullOrEmpty(managedFile.Directory)
            || !string.IsNullOrEmpty(managedFile.Glob)
        );

    private static PackManifest.PackHooks? NormalizeHooks(PackManifest.PackHooks? hooks) =>
        hooks is null
            ? null
            : hooks with
            {
                PostInstall = NormalizeHooks(hooks.PostInstall),
                PostUninstall = NormalizeHooks(hooks.PostUninstall),
                PostUpdate = NormalizeHooks(hooks.PostUpdate),
                PreInstall = NormalizeHooks(hooks.PreInstall),
                PreUninstall = NormalizeHooks(hooks.PreUninstall),
                PreUpdate = NormalizeHooks(hooks.PreUpdate),
            };

    private static List<PackManifest.PackHook>? NormalizeHooks(
        IReadOnlyList<PackManifest.PackHook>? hooks
    ) =>
        hooks is null
            ? null
            :
            [
                .. hooks.Select(hook =>
                    hook with
                    {
                        File = ProjectPath.NormalizeOptional(hook.File),
                    }
                ),
            ];
}
