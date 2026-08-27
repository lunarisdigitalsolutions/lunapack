namespace Lunapack.Cli;

internal static class PackDryRunFormatter
{
    public static IReadOnlyList<string> FormatInstall(PackInstallDryRunResult dryRun)
    {
        var lines = new List<string>(dryRun.UpdatePlan.Actions.Count + 1)
        {
            $"Selected release: {dryRun.SelectedRelease.Id}@{dryRun.SelectedRelease.Version}",
        };
        lines.AddRange(dryRun.UpdatePlan.Actions.Select(FormatAction));
        lines.AddRange(FormatLifecycle(dryRun.UpdatePlan.Lifecycle));
        return lines;
    }

    public static IReadOnlyList<string> FormatUpdate(
        IReadOnlyList<PackUpdateService.UpdateOutcome> outcomes,
        PackUpdatePlan updatePlan,
        LockedSourceUpdateSelector.SourceSwitch? proposedSourceSwitch = null
    )
    {
        var lines = new List<string>(outcomes.Count + updatePlan.Actions.Count);
        if (outcomes.Count == 0)
        {
            lines.Add("No updates are available.");
        }
        else
        {
            lines.AddRange(outcomes.Select(FormatOutcome));
        }

        lines.AddRange(updatePlan.Actions.Select(FormatAction));
        lines.AddRange(FormatLifecycle(updatePlan.Lifecycle));
        if (proposedSourceSwitch is not null)
        {
            lines.Add(
                $"proposed source switch: {proposedSourceSwitch.PackId} {SourceOutputFormatter.FormatIdentity(proposedSourceSwitch.CurrentSource)} -> {SourceOutputFormatter.FormatIdentity(proposedSourceSwitch.SelectedSource)}"
            );
        }
        return lines;
    }

    private static string FormatOutcome(PackUpdateService.UpdateOutcome outcome) =>
        outcome.IsCurrent
            ? $"{outcome.Id} {outcome.CurrentVersion} is current."
            : $"{outcome.Id} {outcome.CurrentVersion} -> {outcome.SelectedVersion}";

    private static string FormatAction(PlannedPackUpdateAction action) =>
        action switch
        {
            CreateManagedFileUpdateAction => $"create {action.TargetPathRelativeToProject}",
            CopyManagedFileUpdateAction => $"copy {action.TargetPathRelativeToProject}",
            BackupAndCopyManagedFileUpdateAction backupAndCopy =>
                $"backup-and-copy {action.TargetPathRelativeToProject} -> {backupAndCopy.BackupPath}",
            MergeLinesManagedFileUpdateAction =>
                $"merge-lines {action.TargetPathRelativeToProject}",
            MergeSectionManagedFileUpdateAction =>
                $"merge-section {action.TargetPathRelativeToProject}",
            MergeJsonManagedFileUpdateAction => $"merge-json {action.TargetPathRelativeToProject}",
            SkipManagedFileUpdateAction => $"skip {action.TargetPathRelativeToProject}",
            DeleteManagedFileUpdateAction => $"delete {action.TargetPathRelativeToProject}",
            _ => throw new ArgumentOutOfRangeException(
                nameof(action),
                action.GetType().Name,
                "Unsupported planned pack update action."
            ),
        };

    private static List<string> FormatLifecycle(LifecycleDryRunPlan? lifecycle)
    {
        if (lifecycle is null)
        {
            return [];
        }

        var lines = new List<string> { $"scripts: {lifecycle.ScriptMode.Value}" };
        lines.AddRange(
            lifecycle.PreMutation.Select(hook => FormatHook("pre-hook", hook, lifecycle.ScriptMode))
        );
        lines.AddRange(
            lifecycle.PostMutation.Select(hook =>
                FormatHook("post-hook", hook, lifecycle.ScriptMode)
            )
        );
        lines.AddRange(
            lifecycle
                .Changes.Where(change => change.DisabledHooks.Count > 0)
                .Select(change =>
                    $"suppressed: {change.IncomingPack!.Manifest.Id}@{change.IncomingPack.Manifest.Version} {string.Join(", ", change.DisabledHooks.OrderBy(value => value, StringComparer.Ordinal))}"
                )
        );
        lines.AddRange(
            lifecycle
                .Changes.Where(change => change.PreviousPack?.SourceIdentity is not null)
                .Select(change =>
                    $"locked source: {change.PreviousPack!.Id} {SourceOutputFormatter.FormatIdentity(change.PreviousPack.SourceIdentity!)}"
                )
        );
        return lines;
    }

    private static string FormatHook(
        string phase,
        LifecycleHookInvocation hook,
        ScriptExecutionMode scriptMode
    )
    {
        var prefix =
            $"{phase}: {hook.Pack.Manifest.Id}@{hook.Pack.Manifest.Version} {LifecycleHookPlanner.ToManifestValue(hook.Hook)}";
        return hook.Instruction is { } instruction
            ? $"{prefix} instruction file: {instruction.PackedFile.RelativePath} templating: {(instruction.Templating ? "enabled" : "disabled")} steps: {instruction.Document.Steps.Count}"
            : $"{prefix} script consent: {GetConsentStatus(scriptMode)}";
    }

    private static string GetConsentStatus(ScriptExecutionMode scriptMode) =>
        scriptMode == ScriptExecutionMode.Skip ? "skipped"
        : scriptMode == ScriptExecutionMode.Run ? "invocation-approved"
        : "trust-or-confirm";
}
