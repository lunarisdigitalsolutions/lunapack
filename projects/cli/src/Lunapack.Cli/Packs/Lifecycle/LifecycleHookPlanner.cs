using System.IO.Abstractions;
using Lunapack.Cli.Application.CommandExecution;
using Lunapack.Cli.Catalog;
using Lunapack.Cli.Packs.Instructions;
using Lunapack.Cli.Packs.Manifest;
using Lunapack.Cli.Packs.Planning;

namespace Lunapack.Cli.Packs.Lifecycle;

internal sealed class LifecycleHookPlanner(IFileSystem fileSystem)
{
    private readonly InstructionPreparer _instructionPreparer = new(fileSystem);

    public ManifestOperationResult<IReadOnlyList<LifecycleHookInvocation>> PlanPreMutation(
        PackLifecyclePlan plan,
        ResolvedPackParameters parameters,
        bool skipInstructions = false
    ) => Plan(plan.PreMutation, parameters, isPreMutation: true, skipInstructions);

    public ManifestOperationResult<IReadOnlyList<LifecycleHookInvocation>> PlanPostMutation(
        PackLifecyclePlan plan,
        ResolvedPackParameters parameters,
        bool skipInstructions = false
    ) => Plan(plan.PostMutation, parameters, isPreMutation: false, skipInstructions);

    private ManifestOperationResult<IReadOnlyList<LifecycleHookInvocation>> Plan(
        IReadOnlyList<PackLifecyclePlan.Entry> changes,
        ResolvedPackParameters parameters,
        bool isPreMutation,
        bool skipInstructions
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

            var declarations = GetHooks(pack.Manifest.Hooks, hook.Value);
            for (var index = 0; index < declarations.Count; index++)
            {
                var declaration = declarations[index];
                if (string.Equals(declaration.Type, "instruction", StringComparison.Ordinal))
                {
                    if (skipInstructions)
                    {
                        continue;
                    }
                }

                var planned = PlanDeclaration(pack, hook.Value, declaration, parameters, index + 1);
                if (planned.Value is not { } invocation)
                {
                    return ManifestOperationResult<IReadOnlyList<LifecycleHookInvocation>>.Failure(
                        planned.Error ?? "Unable to plan lifecycle hook."
                    );
                }

                invocations.Add(invocation);
            }
        }

        return ManifestOperationResult<IReadOnlyList<LifecycleHookInvocation>>.Success(invocations);
    }

    private ManifestOperationResult<LifecycleHookInvocation> PlanDeclaration(
        DiscoveredPack pack,
        LifecycleHook hook,
        PackManifest.PackHook declaration,
        ResolvedPackParameters parameters,
        int position
    ) =>
        string.Equals(declaration.Type, "instruction", StringComparison.Ordinal)
            ? PlanInstruction(pack, hook, declaration, parameters, position)
            : PlanScript(pack, hook, declaration, parameters, position);

    private ManifestOperationResult<LifecycleHookInvocation> PlanInstruction(
        DiscoveredPack pack,
        LifecycleHook hook,
        PackManifest.PackHook declaration,
        ResolvedPackParameters parameters,
        int position
    )
    {
        var prepared = _instructionPreparer.Prepare(pack, declaration, parameters);
        return prepared.Value is { } instruction
            ? ManifestOperationResult<LifecycleHookInvocation>.Success(
                new LifecycleHookInvocation(
                    pack,
                    hook,
                    declaration,
                    instruction.PackedFile,
                    position,
                    instruction
                )
            )
            : ManifestOperationResult<LifecycleHookInvocation>.Failure(
                prepared.Error ?? "Unable to prepare lifecycle instruction."
            );
    }

    private ManifestOperationResult<LifecycleHookInvocation> PlanScript(
        DiscoveredPack pack,
        LifecycleHook hook,
        PackManifest.PackHook script,
        ResolvedPackParameters parameters,
        int position
    )
    {
        var renderedScript = RenderArguments(pack, hook, script, parameters);
        if (renderedScript.Value is not { } invocationScript)
        {
            return ManifestOperationResult<LifecycleHookInvocation>.Failure(
                renderedScript.Error ?? "Unable to render lifecycle hook arguments."
            );
        }

        PackedHookFile? packedFile = null;
        if (invocationScript.File is not null)
        {
            var resolvedFile = PackedHookFile.Resolve(fileSystem, pack, invocationScript.File);
            if (resolvedFile.Value is not { } file)
            {
                return ManifestOperationResult<LifecycleHookInvocation>.Failure(
                    resolvedFile.Error ?? "Unable to bind packed lifecycle hook file."
                );
            }

            packedFile = file;
        }

        return ManifestOperationResult<LifecycleHookInvocation>.Success(
            new LifecycleHookInvocation(pack, hook, invocationScript, packedFile, position)
        );
    }

    private static ManifestOperationResult<PackManifest.PackHook> RenderArguments(
        DiscoveredPack pack,
        LifecycleHook hook,
        PackManifest.PackHook script,
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
                return ManifestOperationResult<PackManifest.PackHook>.Failure(
                    rendered.Error
                        ?? $"Lifecycle script argument '{templateName}' cannot be rendered."
                );
            }

            arguments.Add(argument);
        }

        return ManifestOperationResult<PackManifest.PackHook>.Success(
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
            (PackLifecyclePlan.ChangeKind.Removed, true) => LifecycleHook.PreUninstall,
            (PackLifecyclePlan.ChangeKind.Removed, false) => LifecycleHook.PostUninstall,
            _ => null,
        };

    private static List<PackManifest.PackHook> GetHooks(
        PackManifest.PackHooks? hooks,
        LifecycleHook hook
    ) =>
        hook switch
        {
            LifecycleHook.PreInstall => hooks?.PreInstall ?? [],
            LifecycleHook.PostInstall => hooks?.PostInstall ?? [],
            LifecycleHook.PostUninstall => hooks?.PostUninstall ?? [],
            LifecycleHook.PreUpdate => hooks?.PreUpdate ?? [],
            LifecycleHook.PostUpdate => hooks?.PostUpdate ?? [],
            LifecycleHook.PreUninstall => hooks?.PreUninstall ?? [],
            _ => throw new InvalidOperationException($"Unsupported lifecycle hook '{hook}'."),
        };

    public static string ToManifestValue(LifecycleHook hook) =>
        hook switch
        {
            LifecycleHook.PreInstall => "preInstall",
            LifecycleHook.PostInstall => "postInstall",
            LifecycleHook.PostUninstall => "postUninstall",
            LifecycleHook.PreUpdate => "preUpdate",
            LifecycleHook.PostUpdate => "postUpdate",
            LifecycleHook.PreUninstall => "preUninstall",
            _ => throw new InvalidOperationException($"Unsupported lifecycle hook '{hook}'."),
        };
}
