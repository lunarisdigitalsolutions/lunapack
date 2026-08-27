namespace Lunapack.Cli;

internal sealed class ExternalSourceRequirementPlanner(
    GitRefResolver gitRefResolver,
    ManagedFileConditionParser conditionParser
)
{
    public async Task<ManifestOperationResult<ExternalSourceRequirementPlan>> PlanAsync(
        ResolvedPackGraph graph,
        ProjectConfiguration configuration,
        ResolvedPackParameters parameters,
        CancellationToken cancellationToken = default
    )
    {
        var collected = await CollectAsync(graph, parameters, cancellationToken);
        if (collected.Value is not { } groups)
        {
            return ManifestOperationResult<ExternalSourceRequirementPlan>.Failure(
                collected.Error ?? "Unable to collect external source requirements."
            );
        }

        var workspaceSources = CreateWorkspaceSourceIndex(configuration);
        if (workspaceSources.Value is not { } sourceIndex)
        {
            return ManifestOperationResult<ExternalSourceRequirementPlan>.Failure(
                workspaceSources.Error ?? "Unable to index workspace sources."
            );
        }

        var plannedGroups = CreatePlannedGroups(graph, groups, sourceIndex);
        var mappings = CreateMappings(plannedGroups);
        return ManifestOperationResult<ExternalSourceRequirementPlan>.Success(
            new ExternalSourceRequirementPlan(plannedGroups, mappings)
        );
    }

    private static List<ExternalSourceRequirementGroup> CreatePlannedGroups(
        ResolvedPackGraph graph,
        IReadOnlyDictionary<string, RequirementGroupBuilder> groups,
        WorkspaceSourceIndex sourceIndex
    )
    {
        var plannedGroups = new List<ExternalSourceRequirementGroup>(groups.Count);
        var proposedIdentifiers = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (
            var group in groups.Values.OrderBy(
                group => group.Fingerprint.Value,
                StringComparer.Ordinal
            )
        )
        {
            if (sourceIndex.ByFingerprint.TryGetValue(group.Fingerprint.Value, out var existing))
            {
                plannedGroups.Add(group.ToPlan(existing.Name, true, null));
                continue;
            }

            var identifier = SelectProposedIdentifier(graph, group.Uses);
            string? conflict = null;
            if (sourceIndex.ByName.TryGetValue(identifier, out var configured))
            {
                conflict = configured.Fingerprint.Value;
            }
            else if (
                proposedIdentifiers.TryGetValue(identifier, out var proposedFingerprint)
                && !string.Equals(
                    proposedFingerprint,
                    group.Fingerprint.Value,
                    StringComparison.Ordinal
                )
            )
            {
                conflict = proposedFingerprint;
            }

            proposedIdentifiers.TryAdd(identifier, group.Fingerprint.Value);
            plannedGroups.Add(group.ToPlan(identifier, false, conflict));
        }

        return plannedGroups;
    }

    private static ExternalSourceAliasMapping[] CreateMappings(
        IReadOnlyList<ExternalSourceRequirementGroup> plannedGroups
    ) =>
        plannedGroups
            .SelectMany(group =>
                group.Uses.Select(use => new ExternalSourceAliasMapping(
                    use.PackId,
                    use.PackVersion,
                    use.Alias,
                    group.WorkspaceSourceName,
                    group.Fingerprint
                ))
            )
            .OrderBy(mapping => mapping.PackId, StringComparer.Ordinal)
            .ThenBy(mapping => mapping.Alias, StringComparer.Ordinal)
            .ToArray();

    private async Task<
        ManifestOperationResult<Dictionary<string, RequirementGroupBuilder>>
    > CollectAsync(
        ResolvedPackGraph graph,
        ResolvedPackParameters parameters,
        CancellationToken cancellationToken
    )
    {
        var groups = new Dictionary<string, RequirementGroupBuilder>(StringComparer.Ordinal);
        foreach (var pack in graph.Packs)
        {
            var collected = await CollectPackAsync(pack, parameters, groups, cancellationToken);
            if (!collected.IsSuccess)
            {
                return ManifestOperationResult<Dictionary<string, RequirementGroupBuilder>>.Failure(
                    collected.Error ?? "Unable to collect external source requirements."
                );
            }
        }

        return ManifestOperationResult<Dictionary<string, RequirementGroupBuilder>>.Success(groups);
    }

    private async Task<ManifestOperationResult<bool>> CollectPackAsync(
        DiscoveredPack pack,
        ResolvedPackParameters parameters,
        IDictionary<string, RequirementGroupBuilder> groups,
        CancellationToken cancellationToken
    )
    {
        foreach (
            var groupedFiles in pack.Manifest.ManagedFiles.GroupBy(
                file => PackManagedFileSelector.Create(file).Value?.SourceAlias,
                StringComparer.Ordinal
            )
        )
        {
            if (groupedFiles.Key is not { } alias)
            {
                continue;
            }

            var collected = await CollectAliasAsync(
                pack,
                alias,
                groupedFiles,
                parameters,
                groups,
                cancellationToken
            );
            if (!collected.IsSuccess)
            {
                return collected;
            }
        }

        return ManifestOperationResult<bool>.Success(true);
    }

    private async Task<ManifestOperationResult<bool>> CollectAliasAsync(
        DiscoveredPack pack,
        string alias,
        IEnumerable<PackManifest.PackManagedFile> managedFiles,
        ResolvedPackParameters parameters,
        IDictionary<string, RequirementGroupBuilder> groups,
        CancellationToken cancellationToken
    )
    {
        var selected = CountSelected(managedFiles, parameters);
        if (!selected.IsSuccess)
        {
            return ManifestOperationResult<bool>.Failure(
                selected.Error ?? "Unable to evaluate managed-file condition."
            );
        }

        var selectedCount = selected.Value;
        if (selectedCount == 0)
        {
            return ManifestOperationResult<bool>.Success(true);
        }

        if (!pack.Manifest.Sources.TryGetValue(alias, out var declaration))
        {
            return ManifestOperationResult<bool>.Failure(
                $"Pack '{pack.Manifest.Id}' references undeclared source alias '{alias}'."
            );
        }

        var canonical = await gitRefResolver.ResolveCanonicalRefAsync(
            declaration.Url,
            declaration.Ref,
            timeout: null,
            cancellationToken
        );
        if (canonical.Value is not { } canonicalRef)
        {
            return ManifestOperationResult<bool>.Failure(
                canonical.Error ?? $"Unable to resolve source '{alias}'."
            );
        }

        var fingerprintResult = SourceIdentityNormalizer.CreateGit(
            declaration.Url,
            canonicalRef.CanonicalRef,
            declaration.Path
        );
        if (fingerprintResult.Value is not { } fingerprint)
        {
            return ManifestOperationResult<bool>.Failure(
                fingerprintResult.Error ?? $"Unable to normalize source '{alias}'."
            );
        }

        AddRequirement(pack, alias, declaration, fingerprint, selectedCount, groups);
        return ManifestOperationResult<bool>.Success(true);
    }

    private ManifestOperationResult<int> CountSelected(
        IEnumerable<PackManifest.PackManagedFile> managedFiles,
        ResolvedPackParameters parameters
    )
    {
        var selectedCount = 0;
        foreach (var file in managedFiles)
        {
            var selected = IsSelected(file, parameters);
            if (!selected.IsSuccess)
            {
                return ManifestOperationResult<int>.Failure(
                    selected.Error ?? "Unable to evaluate managed-file condition."
                );
            }

            selectedCount += selected.Value ? 1 : 0;
        }

        return ManifestOperationResult<int>.Success(selectedCount);
    }

    private static void AddRequirement(
        DiscoveredPack pack,
        string alias,
        PackManifest.PackSource declaration,
        SourceFingerprint fingerprint,
        int selectedCount,
        IDictionary<string, RequirementGroupBuilder> groups
    )
    {
        if (!groups.TryGetValue(fingerprint.Value, out var group))
        {
            group = new RequirementGroupBuilder(
                fingerprint,
                new ProjectConfiguration.GitSource
                {
                    Name = alias,
                    Url = declaration.Url,
                    Ref = fingerprint.Ref,
                    Path = ProjectPath.NormalizeOptional(declaration.Path)?.Trim('/'),
                }
            );
            groups.Add(fingerprint.Value, group);
        }

        group.Uses.Add(
            new ExternalSourceRequirementUse(
                pack.Manifest.Id,
                pack.Manifest.Version,
                alias,
                declaration.Description,
                selectedCount
            )
        );
    }

    private ManifestOperationResult<bool> IsSelected(
        PackManifest.PackManagedFile managedFile,
        ResolvedPackParameters parameters
    )
    {
        if (managedFile.Condition is null)
        {
            return ManifestOperationResult<bool>.Success(true);
        }

        var parsed = conditionParser.Parse(managedFile.Condition, parameters.Declarations);
        return parsed.Value is { } condition
            ? ManifestOperationResult<bool>.Success(condition.Evaluate(parameters.Values))
            : ManifestOperationResult<bool>.Failure(
                parsed.Error ?? "Unable to parse managed-file condition."
            );
    }

    private static ManifestOperationResult<WorkspaceSourceIndex> CreateWorkspaceSourceIndex(
        ProjectConfiguration configuration
    )
    {
        var byFingerprint = new Dictionary<string, WorkspaceSource>(StringComparer.Ordinal);
        var byName = new Dictionary<string, WorkspaceSource>(StringComparer.Ordinal);
        foreach (var source in configuration.Sources)
        {
            var created = SourceIdentityNormalizer.Create(source);
            if (created.Value is not { } fingerprint)
            {
                return ManifestOperationResult<WorkspaceSourceIndex>.Failure(
                    created.Error ?? $"Unable to normalize workspace source '{source.Name}'."
                );
            }

            var indexed = new WorkspaceSource(source.Name, fingerprint);
            if (!byFingerprint.TryAdd(fingerprint.Value, indexed))
            {
                return ManifestOperationResult<WorkspaceSourceIndex>.Failure(
                    $"Workspace source fingerprint '{fingerprint}' is configured more than once."
                );
            }

            byName[source.Name] = indexed;
        }

        return ManifestOperationResult<WorkspaceSourceIndex>.Success(
            new WorkspaceSourceIndex(byFingerprint, byName)
        );
    }

    private static string SelectProposedIdentifier(
        ResolvedPackGraph graph,
        IReadOnlyList<ExternalSourceRequirementUse> uses
    ) =>
        uses.OrderBy(use => graph.RootPackIds?.Contains(use.PackId) is true ? 0 : 1)
            .ThenBy(use => use.PackId, StringComparer.Ordinal)
            .ThenBy(use => use.Alias, StringComparer.Ordinal)
            .First()
            .Alias;

    private sealed class RequirementGroupBuilder(
        SourceFingerprint fingerprint,
        ProjectConfiguration.GitSource source
    )
    {
        public SourceFingerprint Fingerprint { get; } = fingerprint;

        public ProjectConfiguration.GitSource Source { get; } = source;

        public List<ExternalSourceRequirementUse> Uses { get; } = [];

        public ExternalSourceRequirementGroup ToPlan(
            string workspaceSourceName,
            bool isExisting,
            string? identifierConflict
        ) =>
            new(
                Fingerprint,
                Source with
                {
                    Name = workspaceSourceName,
                },
                [
                    .. Uses.OrderBy(use => use.PackId, StringComparer.Ordinal)
                        .ThenBy(use => use.Alias, StringComparer.Ordinal),
                ],
                workspaceSourceName,
                isExisting,
                identifierConflict
            );
    }

    private sealed record WorkspaceSource(string Name, SourceFingerprint Fingerprint);

    private sealed record WorkspaceSourceIndex(
        IReadOnlyDictionary<string, WorkspaceSource> ByFingerprint,
        IReadOnlyDictionary<string, WorkspaceSource> ByName
    );
}
