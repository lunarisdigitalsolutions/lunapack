using System.IO.Abstractions;
using Lunapack.Cli.Application.CommandExecution;
using Lunapack.Cli.Application.Preferences;
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
        bool skipInstructions = false,
        ResourceLimits? resourceLimits = null,
        RenderedOutputBudget? outputBudget = null
    )
    {
        var limits = resourceLimits ?? ResourceLimits.Default;
        return Plan(
            plan.PreMutation,
            parameters,
            isPreMutation: true,
            skipInstructions,
            limits,
            outputBudget ?? new RenderedOutputBudget(limits)
        );
    }

    public ManifestOperationResult<IReadOnlyList<LifecycleHookInvocation>> PlanPostMutation(
        PackLifecyclePlan plan,
        ResolvedPackParameters parameters,
        bool skipInstructions = false,
        ResourceLimits? resourceLimits = null,
        RenderedOutputBudget? outputBudget = null
    )
    {
        var limits = resourceLimits ?? ResourceLimits.Default;
        return Plan(
            plan.PostMutation,
            parameters,
            isPreMutation: false,
            skipInstructions,
            limits,
            outputBudget ?? new RenderedOutputBudget(limits)
        );
    }

    private ManifestOperationResult<IReadOnlyList<LifecycleHookInvocation>> Plan(
        IReadOnlyList<PackLifecyclePlan.Entry> changes,
        ResolvedPackParameters parameters,
        bool isPreMutation,
        bool skipInstructions,
        ResourceLimits resourceLimits,
        RenderedOutputBudget outputBudget
    )
    {
        var invocations = new List<LifecycleHookInvocation>();
        foreach (var change in changes)
        {
            var changePlan = PlanChange(
                change,
                parameters,
                isPreMutation,
                skipInstructions,
                resourceLimits,
                outputBudget
            );
            if (changePlan.Value is not { } plannedInvocations)
            {
                return ManifestOperationResult<IReadOnlyList<LifecycleHookInvocation>>.Failure(
                    changePlan.Error ?? "Unable to plan lifecycle hook.",
                    changePlan.ErrorKind
                );
            }

            invocations.AddRange(plannedInvocations);
        }

        return ManifestOperationResult<IReadOnlyList<LifecycleHookInvocation>>.Success(invocations);
    }

    private ManifestOperationResult<IReadOnlyList<LifecycleHookInvocation>> PlanChange(
        PackLifecyclePlan.Entry change,
        ResolvedPackParameters parameters,
        bool isPreMutation,
        bool skipInstructions,
        ResourceLimits resourceLimits,
        RenderedOutputBudget outputBudget
    )
    {
        var invocations = new List<LifecycleHookInvocation>();
        if (change.IncomingPack is not { } pack)
        {
            return ManifestOperationResult<IReadOnlyList<LifecycleHookInvocation>>.Success(
                invocations
            );
        }

        var hook = GetHook(change.Kind, isPreMutation);
        if (hook is null || change.DisabledHooks.Contains(ToManifestValue(hook.Value)))
        {
            return ManifestOperationResult<IReadOnlyList<LifecycleHookInvocation>>.Success(
                invocations
            );
        }

        var declarations = GetHooks(pack.Manifest.Hooks, hook.Value);
        LifecycleScriptState? plannedPreviousScriptState = null;
        for (var index = 0; index < declarations.Count; index++)
        {
            var declaration = declarations[index];
            var included = IsIncluded(declaration, parameters);
            if (!included.IsSuccess || included.Value is not { } conditionPlan)
            {
                return ManifestOperationResult<IReadOnlyList<LifecycleHookInvocation>>.Failure(
                    included.Error
                        ?? $"Unable to evaluate {ToManifestValue(hook.Value)} hook condition.",
                    included.ErrorKind
                );
            }

            if (!conditionPlan.Included)
            {
                if (string.Equals(declaration.Type, "script", StringComparison.Ordinal))
                {
                    plannedPreviousScriptState = LifecycleScriptState.Ignored;
                }

                continue;
            }

            if (
                skipInstructions
                && string.Equals(declaration.Type, "instruction", StringComparison.Ordinal)
            )
            {
                continue;
            }

            var planned = PlanDeclaration(
                pack,
                hook.Value,
                declaration,
                parameters,
                index + 1,
                conditionPlan.RuntimeCondition,
                plannedPreviousScriptState,
                resourceLimits,
                outputBudget
            );
            if (planned.Value is not { } invocation)
            {
                return ManifestOperationResult<IReadOnlyList<LifecycleHookInvocation>>.Failure(
                    planned.Error ?? "Unable to plan lifecycle hook.",
                    planned.ErrorKind
                );
            }

            invocations.Add(invocation);
            if (string.Equals(declaration.Type, "script", StringComparison.Ordinal))
            {
                plannedPreviousScriptState = null;
            }
        }

        return ManifestOperationResult<IReadOnlyList<LifecycleHookInvocation>>.Success(invocations);
    }

    private static ManifestOperationResult<ConditionPlan> IsIncluded(
        PackManifest.PackHook declaration,
        ResolvedPackParameters parameters
    )
    {
        if (declaration.Condition is not { } condition)
        {
            return ManifestOperationResult<ConditionPlan>.Success(new(true, null));
        }

        var parsed = ManagedFileConditionParser.ParseLifecycle(condition, parameters.Declarations);
        return parsed.Value is { } parsedCondition
            ? ManifestOperationResult<ConditionPlan>.Success(
                parsedCondition.DependsOnRuntimeState
                    ? new(true, parsedCondition)
                    : new(parsedCondition.Evaluate(parameters.Values), null)
            )
            : ManifestOperationResult<ConditionPlan>.Failure(
                parsed.Error ?? "Unable to parse lifecycle hook condition."
            );
    }

    private ManifestOperationResult<LifecycleHookInvocation> PlanDeclaration(
        DiscoveredPack pack,
        LifecycleHook hook,
        PackManifest.PackHook declaration,
        ResolvedPackParameters parameters,
        int position,
        ManagedFileCondition? runtimeCondition,
        LifecycleScriptState? plannedPreviousScriptState,
        ResourceLimits resourceLimits,
        RenderedOutputBudget outputBudget
    ) =>
        string.Equals(declaration.Type, "instruction", StringComparison.Ordinal)
            ? PlanInstruction(
                pack,
                hook,
                declaration,
                parameters,
                position,
                runtimeCondition,
                plannedPreviousScriptState,
                resourceLimits,
                outputBudget
            )
            : PlanScript(
                pack,
                hook,
                declaration,
                parameters,
                position,
                runtimeCondition,
                plannedPreviousScriptState,
                resourceLimits,
                outputBudget
            );

    private ManifestOperationResult<LifecycleHookInvocation> PlanInstruction(
        DiscoveredPack pack,
        LifecycleHook hook,
        PackManifest.PackHook declaration,
        ResolvedPackParameters parameters,
        int position,
        ManagedFileCondition? runtimeCondition,
        LifecycleScriptState? plannedPreviousScriptState,
        ResourceLimits resourceLimits,
        RenderedOutputBudget outputBudget
    )
    {
        var prepared = _instructionPreparer.Prepare(
            pack,
            declaration,
            parameters,
            resourceLimits,
            outputBudget
        );
        return prepared.Value is { } instruction
            ? ManifestOperationResult<LifecycleHookInvocation>.Success(
                new LifecycleHookInvocation(
                    pack,
                    hook,
                    declaration,
                    instruction.PackedFile,
                    position,
                    instruction,
                    runtimeCondition,
                    parameters.Values,
                    plannedPreviousScriptState
                )
            )
            : ManifestOperationResult<LifecycleHookInvocation>.Failure(
                prepared.Error ?? "Unable to prepare lifecycle instruction.",
                prepared.ErrorKind
            );
    }

    private ManifestOperationResult<LifecycleHookInvocation> PlanScript(
        DiscoveredPack pack,
        LifecycleHook hook,
        PackManifest.PackHook script,
        ResolvedPackParameters parameters,
        int position,
        ManagedFileCondition? runtimeCondition,
        LifecycleScriptState? plannedPreviousScriptState,
        ResourceLimits resourceLimits,
        RenderedOutputBudget outputBudget
    )
    {
        var renderedScript = RenderArguments(
            pack,
            hook,
            script,
            parameters,
            resourceLimits,
            outputBudget
        );
        if (renderedScript.Value is not { } invocationScript)
        {
            return ManifestOperationResult<LifecycleHookInvocation>.Failure(
                renderedScript.Error ?? "Unable to render lifecycle hook arguments.",
                renderedScript.ErrorKind
            );
        }

        PackedHookFile? packedFile = null;
        if (invocationScript.File is not null)
        {
            var resolvedFile = PackedHookFile.Resolve(
                fileSystem,
                pack,
                invocationScript.File,
                resourceLimits
            );
            if (resolvedFile.Value is not { } file)
            {
                return ManifestOperationResult<LifecycleHookInvocation>.Failure(
                    resolvedFile.Error ?? "Unable to bind packed lifecycle hook file.",
                    resolvedFile.ErrorKind
                );
            }

            packedFile = file;
        }

        return ManifestOperationResult<LifecycleHookInvocation>.Success(
            new LifecycleHookInvocation(
                pack,
                hook,
                invocationScript,
                packedFile,
                position,
                RuntimeCondition: runtimeCondition,
                ParameterValues: parameters.Values,
                PlannedPreviousScriptState: plannedPreviousScriptState
            )
        );
    }

    private sealed record ConditionPlan(bool Included, ManagedFileCondition? RuntimeCondition);

    private static ManifestOperationResult<PackManifest.PackHook> RenderArguments(
        DiscoveredPack pack,
        LifecycleHook hook,
        PackManifest.PackHook script,
        ResolvedPackParameters parameters,
        ResourceLimits resourceLimits,
        RenderedOutputBudget outputBudget
    )
    {
        var arguments = new List<string>(script.Arguments.Count);
        for (var index = 0; index < script.Arguments.Count; index++)
        {
            var templateName = $"{pack.Manifest.Id} {ToManifestValue(hook)} argument {index + 1}";
            var rendered = PackTemplateRenderer.RenderText(
                script.Arguments[index],
                templateName,
                parameters,
                resourceLimits,
                outputBudget
            );
            if (rendered.Value is not { } argument)
            {
                return ManifestOperationResult<PackManifest.PackHook>.Failure(
                    rendered.Error
                        ?? $"Lifecycle script argument '{templateName}' cannot be rendered.",
                    rendered.ErrorKind
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
