using Lunapack.Cli.Packs.ExternalSources;
using Lunapack.Cli.Packs.Lifecycle;
using Lunapack.Cli.Packs.ManagedFiles;
using Lunapack.Cli.Packs.Planning;
using Lunapack.Cli.Sources;
using Lunapack.Cli.Trust;
using Spectre.Console;

namespace Lunapack.Cli.Packs.Commands;

internal static class PackDryRunFormatter
{
    public static IReadOnlyList<string> FormatInstall(PackInstallDryRunResult dryRun)
    {
        var selectedVersion =
            dryRun.SelectedRelease.Version
            ?? throw new InvalidOperationException(
                "An install plan must select a release version."
            );
        var lines = new List<string>(dryRun.UpdatePlan.Actions.Count + 1)
        {
            "[bold]Install plan[/]",
            $"[cyan]*[/] Selected release  [bold]{Markup.Escape(dryRun.SelectedRelease.Id)}@{Markup.Escape(selectedVersion)}[/]",
        };
        AddSection(
            lines,
            "External sources",
            FormatExternalSources(dryRun.UpdatePlan.ExternalSources)
        );
        AddSection(lines, "File changes", FormatFileChanges(dryRun.UpdatePlan));
        AddSection(lines, "Lifecycle", FormatLifecycle(dryRun.UpdatePlan.Lifecycle));
        return lines;
    }

    public static IReadOnlyList<string> FormatUpdate(
        IReadOnlyList<PackUpdateService.UpdateOutcome> outcomes,
        PackUpdatePlan updatePlan,
        LockedSourceUpdateSelector.SourceSwitch? proposedSourceSwitch = null
    )
    {
        var lines = new List<string>(outcomes.Count + updatePlan.Actions.Count);
        lines.Add("[bold]Update plan[/]");
        if (outcomes.Count == 0)
        {
            lines.Add("[grey]-[/] No updates are available.");
        }
        else
        {
            lines.AddRange(outcomes.Select(FormatOutcome));
        }

        AddSection(lines, "External sources", FormatExternalSources(updatePlan.ExternalSources));
        AddSection(lines, "File changes", FormatFileChanges(updatePlan));
        AddSection(lines, "Lifecycle", FormatLifecycle(updatePlan.Lifecycle));
        if (proposedSourceSwitch is not null)
        {
            AddSection(
                lines,
                "Source switch",
                [
                    $"[yellow]~[/] {Markup.Escape(proposedSourceSwitch.PackId)}  {Markup.Escape(SourceOutputFormatter.FormatIdentity(proposedSourceSwitch.CurrentSource))} -> {Markup.Escape(SourceOutputFormatter.FormatIdentity(proposedSourceSwitch.SelectedSource))}",
                ]
            );
        }
        return lines;
    }

    public static IEnumerable<string> FormatFileChanges(PackUpdatePlan updatePlan) =>
        updatePlan.Actions.Select(FormatAction);

    public static IReadOnlyList<string> FormatAppliedFileChanges(PackUpdatePlan updatePlan)
    {
        var lines = new List<string>();
        AddSection(lines, "File changes", FormatFileChanges(updatePlan));
        return lines;
    }

    private static void AddSection(List<string> lines, string heading, IEnumerable<string> content)
    {
        var sectionLines = content.ToList();
        if (sectionLines.Count == 0)
        {
            return;
        }

        lines.Add(string.Empty);
        lines.Add($"[bold]{heading}[/]");
        lines.AddRange(sectionLines);
    }

    private static IEnumerable<string> FormatExternalSources(
        ExternalSourceRequirementPlan? externalSources
    )
    {
        if (externalSources is null)
        {
            return [];
        }

        return externalSources
            .Mappings.Select(mapping =>
                $"[cyan]>[/] Map  {Markup.Escape(mapping.PackId)}: {Markup.Escape(mapping.Alias)} -> {Markup.Escape(mapping.WorkspaceSourceName)}"
            )
            .Concat(
                externalSources.Proposed.Select(group =>
                    $"[green]+[/] Add  {Markup.Escape(group.WorkspaceSourceName)} git(identity={Markup.Escape(group.Fingerprint.Identity)}, ref={Markup.Escape(group.Fingerprint.Ref ?? string.Empty)}, path={Markup.Escape(group.Fingerprint.Path ?? string.Empty)}) [yellow]approval required[/]"
                )
            );
    }

    private static string FormatOutcome(PackUpdateService.UpdateOutcome outcome) =>
        outcome.IsCurrent
            ? $"[grey]-[/] {Markup.Escape(outcome.Id)}  {Markup.Escape(outcome.CurrentVersion)} is current"
            : $"[cyan]*[/] {Markup.Escape(outcome.Id)}  {Markup.Escape(outcome.CurrentVersion)} -> [bold]{Markup.Escape(outcome.SelectedVersion)}[/]";

    private static string FormatAction(PlannedPackUpdateAction action) =>
        action switch
        {
            CreateManagedFileUpdateAction =>
                $"[green]+[/] Create  {Markup.Escape(action.TargetPathRelativeToProject)}",
            CopyManagedFileUpdateAction =>
                $"[cyan]>[/] Copy    {Markup.Escape(action.TargetPathRelativeToProject)}",
            BackupAndCopyManagedFileUpdateAction backupAndCopy =>
                $"[yellow]![/] Replace {Markup.Escape(action.TargetPathRelativeToProject)}  [grey](backup: {Markup.Escape(backupAndCopy.BackupPath)})[/]",
            MergeLinesManagedFileUpdateAction =>
                $"[yellow]~[/] Merge   {Markup.Escape(action.TargetPathRelativeToProject)} [grey](lines)[/]",
            MergeSectionManagedFileUpdateAction =>
                $"[yellow]~[/] Merge   {Markup.Escape(action.TargetPathRelativeToProject)} [grey](section)[/]",
            MergeJsonManagedFileUpdateAction =>
                $"[yellow]~[/] Merge   {Markup.Escape(action.TargetPathRelativeToProject)} [grey](JSON)[/]",
            SkipManagedFileUpdateAction =>
                $"[grey]=[/] Skip    {Markup.Escape(action.TargetPathRelativeToProject)}",
            DeleteManagedFileUpdateAction =>
                $"[red]-[/] Delete  {Markup.Escape(action.TargetPathRelativeToProject)}",
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

        var lines = new List<string> { $"[magenta]>[/] Scripts    {lifecycle.ScriptMode.Value}" };
        lines.AddRange(
            lifecycle.PreMutation.SelectMany(hook => FormatHook("Pre-hook ", hook, lifecycle))
        );
        lines.AddRange(
            lifecycle.PostMutation.SelectMany(hook => FormatHook("Post-hook", hook, lifecycle))
        );
        foreach (var change in lifecycle.Changes)
        {
            if (change.DisabledHooks.Count > 0)
            {
                var incomingPack =
                    change.IncomingPack
                    ?? throw new InvalidOperationException(
                        "A lifecycle change with disabled hooks must include an incoming pack."
                    );
                var disabledHooks = string.Join(
                    ", ",
                    change.DisabledHooks.OrderBy(value => value, StringComparer.Ordinal)
                );
                lines.Add(
                    $"[grey]-[/] Suppressed  {Markup.Escape(incomingPack.Manifest.Id)}@{Markup.Escape(incomingPack.Manifest.Version)} {Markup.Escape(disabledHooks)}"
                );
            }
        }

        return lines;
    }

    private static IEnumerable<string> FormatHook(
        string phase,
        LifecycleHookInvocation hook,
        LifecycleDryRunPlan lifecycle
    )
    {
        yield return $"[magenta]>[/] {phase}  {Markup.Escape(hook.Pack.Manifest.Id)}@{Markup.Escape(hook.Pack.Manifest.Version)}";
        if (hook.Instruction is { } instruction)
        {
            yield return $"    Instruction  {Markup.Escape(instruction.PackedFile.RelativePath)}";
            yield return $"    Templating   {(instruction.Templating ? "enabled" : "disabled")}";
            yield return $"    Steps        {instruction.Document.Steps.Count}";
        }
        else
        {
            yield return $"    Script       {GetConsentStatus(lifecycle, hook)}";
        }
    }

    private static string GetConsentStatus(
        LifecycleDryRunPlan lifecycle,
        LifecycleHookInvocation hook
    ) =>
        lifecycle.ScriptDenialScopes is { Count: > 0 } denyingScopes
            ? $"blocked (policy: {string.Join(", ", denyingScopes.Select(ScriptDenialOriginFormatter.Format))})"
        : lifecycle.ScriptMode == ScriptExecutionMode.Skip ? "skipped (--scripts skip)"
        : lifecycle.ScriptMode == ScriptExecutionMode.Run ? "allowed (--scripts run)"
        : lifecycle.ScriptTrustScopes?.TryGetValue(hook, out var trustScopes) == true
        && trustScopes.Count > 0
            ? $"allowed (trust: {string.Join(", ", trustScopes.Select(TrustOutputFormatter.FormatScope))})"
        : "confirmation required";
}
