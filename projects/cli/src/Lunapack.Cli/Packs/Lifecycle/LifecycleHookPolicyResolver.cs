using Lunapack.Cli.Trust;

namespace Lunapack.Cli.Packs.Lifecycle;

internal static class LifecycleHookPolicyResolver
{
    public const int DefaultTimeoutSeconds = 300;

    private static readonly string[] _commonEnvironmentVariables =
    [
        "PATH",
        "TEMP",
        "TMP",
        "TMPDIR",
    ];

    private static readonly string[] _windowsEnvironmentVariables =
    [
        "PATHEXT",
        "SYSTEMROOT",
        "WINDIR",
        "COMSPEC",
        "USERPROFILE",
        "HOMEDRIVE",
        "HOMEPATH",
        "APPDATA",
        "LOCALAPPDATA",
        "SYSTEMDRIVE",
    ];

    private static readonly string[] _unixEnvironmentVariables =
    [
        "HOME",
        "SHELL",
        "LANG",
        "LC_ALL",
        "LC_CTYPE",
        "TERM",
    ];

    public static EffectiveLifecycleHookPolicy Resolve(
        LifecycleHookPolicy? projectPolicy,
        LifecycleHookPolicy? localProjectPolicy,
        LifecycleHookPolicy? globalUserPolicy
    ) =>
        OperatingSystem.IsWindows()
            ? ResolveForWindows(projectPolicy, localProjectPolicy, globalUserPolicy)
            : ResolveForUnix(projectPolicy, localProjectPolicy, globalUserPolicy);

    internal static EffectiveLifecycleHookPolicy ResolveForWindows(
        LifecycleHookPolicy? projectPolicy,
        LifecycleHookPolicy? localProjectPolicy,
        LifecycleHookPolicy? globalUserPolicy
    ) =>
        ResolveForPlatform(
            projectPolicy,
            localProjectPolicy,
            globalUserPolicy,
            StringComparer.OrdinalIgnoreCase,
            _windowsEnvironmentVariables
        );

    internal static EffectiveLifecycleHookPolicy ResolveForUnix(
        LifecycleHookPolicy? projectPolicy,
        LifecycleHookPolicy? localProjectPolicy,
        LifecycleHookPolicy? globalUserPolicy
    ) =>
        ResolveForPlatform(
            projectPolicy,
            localProjectPolicy,
            globalUserPolicy,
            StringComparer.Ordinal,
            _unixEnvironmentVariables
        );

    private static EffectiveLifecycleHookPolicy ResolveForPlatform(
        LifecycleHookPolicy? projectPolicy,
        LifecycleHookPolicy? localProjectPolicy,
        LifecycleHookPolicy? globalUserPolicy,
        StringComparer variableNameComparer,
        IEnumerable<string> platformEnvironmentVariables
    )
    {
        var allowedEnvironmentVariables = new HashSet<string>(
            _commonEnvironmentVariables,
            variableNameComparer
        );
        allowedEnvironmentVariables.UnionWith(platformEnvironmentVariables);

        int? timeoutSeconds = null;
        foreach (var policy in new[] { projectPolicy, localProjectPolicy, globalUserPolicy })
        {
            if (policy is null)
            {
                continue;
            }

            allowedEnvironmentVariables.UnionWith(policy.AllowedEnvironmentVariables);
            if (
                policy.TimeoutSeconds is { } configuredTimeout
                && (timeoutSeconds is null || configuredTimeout < timeoutSeconds)
            )
            {
                timeoutSeconds = configuredTimeout;
            }
        }

        return new EffectiveLifecycleHookPolicy(
            allowedEnvironmentVariables,
            timeoutSeconds ?? DefaultTimeoutSeconds
        );
    }
}
