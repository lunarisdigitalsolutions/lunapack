using System.IO.Abstractions;

namespace Lunapack.Cli;

internal sealed class LifecycleHookPlanner(IFileSystem fileSystem)
{
    public ManifestOperationResult<IReadOnlyList<LifecycleHookInvocation>> PlanPreMutation(
        PackLifecyclePlan plan,
        ResolvedPackParameters parameters
    ) => Plan(plan.PreMutation, parameters, isPreMutation: true);

    public ManifestOperationResult<IReadOnlyList<LifecycleHookInvocation>> PlanPostMutation(
        PackLifecyclePlan plan,
        ResolvedPackParameters parameters
    ) => Plan(plan.PostMutation, parameters, isPreMutation: false);

    private ManifestOperationResult<IReadOnlyList<LifecycleHookInvocation>> Plan(
        IReadOnlyList<PackLifecyclePlan.Entry> changes,
        ResolvedPackParameters parameters,
        bool isPreMutation
    )
    {
        var invocations = new List<LifecycleHookInvocation>();
        foreach (var change in changes)
        {
            if (change.IncomingPack is not { } pack)
            {
                continue;
            }

            var hook = GetHook(change.Kind, isPreMutation);
            if (hook is null || change.DisabledHooks.Contains(ToManifestValue(hook.Value)))
            {
                continue;
            }

            var script = GetScript(pack.Manifest.Scripts, hook.Value);
            if (script is null)
            {
                continue;
            }

            var renderedScript = RenderArguments(pack, hook.Value, script, parameters);
            if (renderedScript.Value is not { } invocationScript)
            {
                return ManifestOperationResult<IReadOnlyList<LifecycleHookInvocation>>.Failure(
                    renderedScript.Error ?? "Unable to render lifecycle hook arguments."
                );
            }

            PackedHookFile? packedFile = null;
            if (invocationScript.File is not null)
            {
                var resolvedFile = PackedHookFile.Resolve(fileSystem, pack, invocationScript.File);
                if (resolvedFile.Value is not { } file)
                {
                    return ManifestOperationResult<IReadOnlyList<LifecycleHookInvocation>>.Failure(
                        resolvedFile.Error ?? "Unable to bind packed lifecycle hook file."
                    );
                }

                packedFile = file;
            }

            invocations.Add(
                new LifecycleHookInvocation(pack, hook.Value, invocationScript, packedFile)
            );
        }

        return ManifestOperationResult<IReadOnlyList<LifecycleHookInvocation>>.Success(invocations);
    }

    private static ManifestOperationResult<PackManifest.LifecycleScript> RenderArguments(
        DiscoveredPack pack,
        LifecycleHook hook,
        PackManifest.LifecycleScript script,
        ResolvedPackParameters parameters
    )
    {
        var arguments = new List<string>(script.Arguments.Count);
        for (var index = 0; index < script.Arguments.Count; index++)
        {
            var templateName = $"{pack.Manifest.Id} {ToManifestValue(hook)} argument {index + 1}";
            var rendered = PackTemplateRenderer.RenderText(
                script.Arguments[index],
                templateName,
                parameters
            );
            if (rendered.Value is not { } argument)
            {
                return ManifestOperationResult<PackManifest.LifecycleScript>.Failure(
                    rendered.Error
                        ?? $"Lifecycle script argument '{templateName}' cannot be rendered."
                );
            }

            arguments.Add(argument);
        }

        return ManifestOperationResult<PackManifest.LifecycleScript>.Success(
            script with
            {
                Arguments = arguments,
            }
        );
    }

    private static LifecycleHook? GetHook(PackLifecyclePlan.ChangeKind kind, bool isPreMutation) =>
        (kind, isPreMutation) switch
        {
            (PackLifecyclePlan.ChangeKind.Install, true) => LifecycleHook.PreInstall,
            (PackLifecyclePlan.ChangeKind.Install, false) => LifecycleHook.PostInstall,
            (PackLifecyclePlan.ChangeKind.Update, true) => LifecycleHook.PreUpdate,
            (PackLifecyclePlan.ChangeKind.Update, false) => LifecycleHook.PostUpdate,
            _ => null,
        };

    private static PackManifest.LifecycleScript? GetScript(
        PackManifest.PackScripts? scripts,
        LifecycleHook hook
    ) =>
        hook switch
        {
            LifecycleHook.PreInstall => scripts?.PreInstall,
            LifecycleHook.PostInstall => scripts?.PostInstall,
            LifecycleHook.PreUpdate => scripts?.PreUpdate,
            LifecycleHook.PostUpdate => scripts?.PostUpdate,
            _ => throw new InvalidOperationException($"Unsupported lifecycle hook '{hook}'."),
        };

    public static string ToManifestValue(LifecycleHook hook) =>
        hook switch
        {
            LifecycleHook.PreInstall => "preInstall",
            LifecycleHook.PostInstall => "postInstall",
            LifecycleHook.PreUpdate => "preUpdate",
            LifecycleHook.PostUpdate => "postUpdate",
            _ => throw new InvalidOperationException($"Unsupported lifecycle hook '{hook}'."),
        };
}
