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
            Hooks = NormalizeHooks(manifest.Hooks),
        };

    private static PackManifest.PackHooks? NormalizeHooks(PackManifest.PackHooks? hooks) =>
        hooks is null
            ? null
            : hooks with
            {
                PostInstall = NormalizeHooks(hooks.PostInstall),
                PostUpdate = NormalizeHooks(hooks.PostUpdate),
                PreInstall = NormalizeHooks(hooks.PreInstall),
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
