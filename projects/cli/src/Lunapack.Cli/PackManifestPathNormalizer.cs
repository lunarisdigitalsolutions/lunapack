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
            Scripts = NormalizeScripts(manifest.Scripts),
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

    private static PackManifest.PackScripts? NormalizeScripts(PackManifest.PackScripts? scripts) =>
        scripts is null
            ? null
            : scripts with
            {
                PostInstall = NormalizeScript(scripts.PostInstall),
                PostUpdate = NormalizeScript(scripts.PostUpdate),
                PreInstall = NormalizeScript(scripts.PreInstall),
                PreUpdate = NormalizeScript(scripts.PreUpdate),
            };

    private static PackManifest.LifecycleScript? NormalizeScript(
        PackManifest.LifecycleScript? script
    ) => script is null ? null : script with { File = ProjectPath.NormalizeOptional(script.File) };
}
