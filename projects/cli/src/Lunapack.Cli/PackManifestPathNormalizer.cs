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
                        Glob = ProjectPath.NormalizeOptional(managedFile.Glob),
                        Source = ProjectPath.NormalizeOptional(managedFile.Source),
                        Target = ProjectPath.Normalize(managedFile.Target),
                    }
                ),
            ],
            Scripts = NormalizeScripts(manifest.Scripts),
        };

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
