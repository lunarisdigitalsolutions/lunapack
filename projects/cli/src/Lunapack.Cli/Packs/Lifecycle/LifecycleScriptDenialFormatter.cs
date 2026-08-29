using Lunapack.Cli.Trust;

namespace Lunapack.Cli.Packs.Lifecycle;

internal static class LifecycleScriptDenialFormatter
{
    public static string Format(PolicyDeniedLifecycleHook denied) =>
        $"Lifecycle script denied by policy: pack {denied.Invocation.Pack.Manifest.Id}@{denied.Invocation.Pack.Manifest.Version}, event {LifecycleHookPlanner.ToManifestValue(denied.Invocation.Hook)}, scopes: {string.Join(", ", denied.DenyingScopes.Select(ScriptDenialOriginFormatter.Format))}.";
}
