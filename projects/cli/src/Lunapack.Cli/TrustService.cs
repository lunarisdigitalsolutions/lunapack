using System.IO.Abstractions;
using System.Text;

namespace Lunapack.Cli;

internal sealed class TrustService(
    IFileSystem fileSystem,
    ProjectStateStore projectStateStore,
    UserSettingsStore userSettingsStore,
    ITrustConfirmer confirmer
)
{
    public async Task<ManifestOperationResult<bool>> TrustSourcesAsync(
        string projectDirectory,
        IReadOnlyList<string> sourceNames,
        TrustScope scope
    )
    {
        var contextResult = await LoadContextAsync(projectDirectory);
        if (contextResult.Value is not { } context)
        {
            return ManifestOperationResult<bool>.Failure(
                contextResult.Error ?? "Unable to load trust state."
            );
        }

        var sourcesResult = ResolveSources(projectDirectory, context.State, sourceNames);
        if (sourcesResult.Value is not { } sources)
        {
            return ManifestOperationResult<bool>.Failure(
                sourcesResult.Error ?? "Unable to resolve sources."
            );
        }

        var updatedState = CloneState(context.State);
        var updatedSettings = CloneSettings(context.Settings);
        if (!ApplySources(scope, context.ProjectKey, sources, updatedState, updatedSettings))
        {
            return ManifestOperationResult<bool>.Success(true);
        }

        if (!confirmer.Confirm(CreateWarning(scope, sources, packIds: null)))
        {
            return ManifestOperationResult<bool>.Failure("Trust was not confirmed interactively.");
        }

        return await PersistAsync(projectDirectory, scope, context, updatedState, updatedSettings);
    }

    public async Task<ManifestOperationResult<bool>> TrustPacksAsync(
        string projectDirectory,
        IReadOnlyList<string> packIds,
        string sourceName,
        TrustScope scope
    )
    {
        if (packIds.Count == 0 || packIds.Any(id => string.IsNullOrEmpty(id) || id.Contains('@')))
        {
            return ManifestOperationResult<bool>.Failure(
                "Pack trust requires one or more bare pack IDs without version selectors."
            );
        }

        var contextResult = await LoadContextAsync(projectDirectory);
        if (contextResult.Value is not { } context)
        {
            return ManifestOperationResult<bool>.Failure(
                contextResult.Error ?? "Unable to load trust state."
            );
        }

        var sourcesResult = ResolveSources(projectDirectory, context.State, [sourceName]);
        if (sourcesResult.Value is not { } sources)
        {
            return ManifestOperationResult<bool>.Failure(
                sourcesResult.Error ?? "Unable to resolve source."
            );
        }

        var uniquePackIds = packIds.Distinct(StringComparer.Ordinal).ToArray();
        var updatedState = CloneState(context.State);
        var updatedSettings = CloneSettings(context.Settings);
        if (
            !ApplyPacks(
                scope,
                context.ProjectKey,
                sources[0],
                uniquePackIds,
                updatedState,
                updatedSettings
            )
        )
        {
            return ManifestOperationResult<bool>.Success(true);
        }

        if (!confirmer.Confirm(CreateWarning(scope, sources, uniquePackIds)))
        {
            return ManifestOperationResult<bool>.Failure("Trust was not confirmed interactively.");
        }

        return await PersistAsync(projectDirectory, scope, context, updatedState, updatedSettings);
    }

    public async Task<ManifestOperationResult<TrustListing>> ListAsync(
        string projectDirectory,
        TrustScope scope
    )
    {
        var contextResult = await LoadContextAsync(projectDirectory);
        if (contextResult.Value is not { } context)
        {
            return ManifestOperationResult<TrustListing>.Failure(
                contextResult.Error ?? "Unable to load trust state."
            );
        }

        context.Settings.Projects.TryGetValue(context.ProjectKey, out var localTrust);
        return ManifestOperationResult<TrustListing>.Success(
            scope switch
            {
                TrustScope.LocalUser => new TrustListing
                {
                    Scope = scope,
                    Sources = localTrust?.Sources ?? [],
                    Packs = localTrust?.Packs ?? [],
                },
                TrustScope.GlobalUser => new TrustListing
                {
                    Scope = scope,
                    Sources = context.Settings.Global.Sources,
                    Packs = context.Settings.Global.Packs,
                },
                TrustScope.Project => new TrustListing
                {
                    Scope = scope,
                    ProjectSourceDeclarations = context.State.Configuration.Trust.Sources,
                    ProjectPackDeclarations = context.State.Configuration.Trust.Packs,
                    ProjectSourceAcknowledgements = localTrust?.Acknowledgements.Sources ?? [],
                    ProjectPackAcknowledgements = localTrust?.Acknowledgements.Packs ?? [],
                },
                _ => throw new ArgumentOutOfRangeException(nameof(scope)),
            }
        );
    }

    public async Task<ManifestOperationResult<bool>> RevokeSourcesAsync(
        string projectDirectory,
        IReadOnlyList<string> sourceNames,
        TrustScope scope
    )
    {
        var contextResult = await LoadContextAsync(projectDirectory);
        if (contextResult.Value is not { } context)
        {
            return ManifestOperationResult<bool>.Failure(
                contextResult.Error ?? "Unable to load trust state."
            );
        }

        var sourcesResult = ResolveSources(projectDirectory, context.State, sourceNames);
        if (sourcesResult.Value is not { } sources)
        {
            return ManifestOperationResult<bool>.Failure(
                sourcesResult.Error ?? "Unable to resolve sources."
            );
        }

        var updatedState = CloneState(context.State);
        var updatedSettings = CloneSettings(context.Settings);
        if (!RemoveSources(scope, context.ProjectKey, sources, updatedState, updatedSettings))
        {
            return ManifestOperationResult<bool>.Success(true);
        }

        return await PersistAsync(projectDirectory, scope, context, updatedState, updatedSettings);
    }

    public async Task<ManifestOperationResult<bool>> RevokePacksAsync(
        string projectDirectory,
        IReadOnlyList<string> packIds,
        string sourceName,
        TrustScope scope
    )
    {
        if (packIds.Count == 0 || packIds.Any(id => string.IsNullOrEmpty(id) || id.Contains('@')))
        {
            return ManifestOperationResult<bool>.Failure(
                "Pack trust requires one or more bare pack IDs without version selectors."
            );
        }

        var contextResult = await LoadContextAsync(projectDirectory);
        if (contextResult.Value is not { } context)
        {
            return ManifestOperationResult<bool>.Failure(
                contextResult.Error ?? "Unable to load trust state."
            );
        }

        var sourcesResult = ResolveSources(projectDirectory, context.State, [sourceName]);
        if (sourcesResult.Value is not { } sources)
        {
            return ManifestOperationResult<bool>.Failure(
                sourcesResult.Error ?? "Unable to resolve source."
            );
        }

        var updatedState = CloneState(context.State);
        var updatedSettings = CloneSettings(context.Settings);
        if (
            !RemovePacks(
                scope,
                context.ProjectKey,
                sources[0],
                packIds.Distinct(StringComparer.Ordinal),
                updatedState,
                updatedSettings
            )
        )
        {
            return ManifestOperationResult<bool>.Success(true);
        }

        return await PersistAsync(projectDirectory, scope, context, updatedState, updatedSettings);
    }

    private async Task<ManifestOperationResult<TrustOperationContext>> LoadContextAsync(
        string projectDirectory
    )
    {
        var stateResult = await projectStateStore.LoadAsync(projectDirectory);
        if (stateResult.Value is not { } state)
        {
            return ManifestOperationResult<TrustOperationContext>.Failure(
                stateResult.Error ?? "Unable to load project state."
            );
        }

        var settingsResult = await userSettingsStore.LoadAsync();
        if (settingsResult.Value is not { } settings)
        {
            return ManifestOperationResult<TrustOperationContext>.Failure(
                settingsResult.Error ?? "Unable to load user settings."
            );
        }

        var projectKeyResult = userSettingsStore.GetProjectKey(projectDirectory);
        return projectKeyResult.Value is { } projectKey
            ? ManifestOperationResult<TrustOperationContext>.Success(
                new TrustOperationContext(state, settings, projectKey)
            )
            : ManifestOperationResult<TrustOperationContext>.Failure(
                projectKeyResult.Error ?? "Unable to identify project directory."
            );
    }

    private ManifestOperationResult<
        List<KeyValuePair<string, ConfiguredSourceIdentity>>
    > ResolveSources(string projectDirectory, ProjectState state, IReadOnlyList<string> sourceNames)
    {
        if (sourceNames.Count == 0 || sourceNames.Any(string.IsNullOrEmpty))
        {
            return ManifestOperationResult<
                List<KeyValuePair<string, ConfiguredSourceIdentity>>
            >.Failure("At least one source name is required.");
        }

        var sources = new List<KeyValuePair<string, ConfiguredSourceIdentity>>();
        foreach (var name in sourceNames.Distinct(StringComparer.Ordinal))
        {
            var source = state.Configuration.Sources.Find(candidate =>
                string.Equals(candidate.Name, name, StringComparison.Ordinal)
            );
            if (source is null)
            {
                return ManifestOperationResult<
                    List<KeyValuePair<string, ConfiguredSourceIdentity>>
                >.Failure($"Source '{name}' is not configured.");
            }

            var identityResult = ResolveIdentity(projectDirectory, source);
            if (identityResult.Value is not { } identity)
            {
                return ManifestOperationResult<
                    List<KeyValuePair<string, ConfiguredSourceIdentity>>
                >.Failure(identityResult.Error ?? $"Unable to resolve source '{name}'.");
            }

            sources.Add(new KeyValuePair<string, ConfiguredSourceIdentity>(name, identity));
        }

        return ManifestOperationResult<
            List<KeyValuePair<string, ConfiguredSourceIdentity>>
        >.Success(sources);
    }

    private ManifestOperationResult<ConfiguredSourceIdentity> ResolveIdentity(
        string projectDirectory,
        ProjectConfiguration.Source source
    ) => ConfiguredSourceIdentity.CreateForTrust(fileSystem, projectDirectory, source);

    private static bool ApplySources(
        TrustScope scope,
        string projectKey,
        IReadOnlyList<KeyValuePair<string, ConfiguredSourceIdentity>> sources,
        ProjectState state,
        UserSettings settings
    )
    {
        var changed = false;
        foreach (var source in sources)
        {
            changed |= scope switch
            {
                TrustScope.LocalUser => AddUnique(
                    GetProjectTrust(settings, projectKey).Sources,
                    source.Value
                ),
                TrustScope.GlobalUser => AddUnique(settings.Global.Sources, source.Value),
                TrustScope.Project => AddProjectSource(
                    state.Configuration.Trust.Sources,
                    source.Key
                )
                    | AddUnique(
                        GetProjectTrust(settings, projectKey).Acknowledgements.Sources,
                        source.Value
                    ),
                _ => throw new ArgumentOutOfRangeException(nameof(scope)),
            };
        }

        return changed;
    }

    private static bool ApplyPacks(
        TrustScope scope,
        string projectKey,
        KeyValuePair<string, ConfiguredSourceIdentity> source,
        IReadOnlyList<string> packIds,
        ProjectState state,
        UserSettings settings
    )
    {
        var changed = false;
        foreach (var packId in packIds)
        {
            var identity = new TrustedPackIdentity { Id = packId, Source = source.Value };
            changed |= scope switch
            {
                TrustScope.LocalUser => AddUnique(
                    GetProjectTrust(settings, projectKey).Packs,
                    identity
                ),
                TrustScope.GlobalUser => AddUnique(settings.Global.Packs, identity),
                TrustScope.Project => AddProjectPack(
                    state.Configuration.Trust.Packs,
                    source.Key,
                    packId
                )
                    | AddUnique(
                        GetProjectTrust(settings, projectKey).Acknowledgements.Packs,
                        identity
                    ),
                _ => throw new ArgumentOutOfRangeException(nameof(scope)),
            };
        }

        return changed;
    }

    private static bool RemoveSources(
        TrustScope scope,
        string projectKey,
        IReadOnlyList<KeyValuePair<string, ConfiguredSourceIdentity>> sources,
        ProjectState state,
        UserSettings settings
    )
    {
        settings.Projects.TryGetValue(projectKey, out var localTrust);
        var changed = false;
        foreach (var source in sources)
        {
            changed |= scope switch
            {
                TrustScope.LocalUser => localTrust?.Sources.Remove(source.Value) == true,
                TrustScope.GlobalUser => settings.Global.Sources.Remove(source.Value),
                TrustScope.Project => state.Configuration.Trust.Sources.Remove(source.Key)
                    | (localTrust?.Acknowledgements.Sources.Remove(source.Value) == true),
                _ => throw new ArgumentOutOfRangeException(nameof(scope)),
            };
        }

        return changed;
    }

    private static bool RemovePacks(
        TrustScope scope,
        string projectKey,
        KeyValuePair<string, ConfiguredSourceIdentity> source,
        IEnumerable<string> packIds,
        ProjectState state,
        UserSettings settings
    )
    {
        settings.Projects.TryGetValue(projectKey, out var localTrust);
        var changed = false;
        foreach (var packId in packIds)
        {
            changed |= scope switch
            {
                TrustScope.LocalUser => RemovePack(localTrust?.Packs, source.Value, packId),
                TrustScope.GlobalUser => RemovePack(settings.Global.Packs, source.Value, packId),
                TrustScope.Project => RemoveProjectPack(
                    state.Configuration.Trust.Packs,
                    source.Key,
                    packId
                ) | RemovePack(localTrust?.Acknowledgements.Packs, source.Value, packId),
                _ => throw new ArgumentOutOfRangeException(nameof(scope)),
            };
        }

        return changed;
    }

    private async Task<ManifestOperationResult<bool>> PersistAsync(
        string projectDirectory,
        TrustScope scope,
        TrustOperationContext original,
        ProjectState updatedState,
        UserSettings updatedSettings
    )
    {
        if (scope is not TrustScope.Project)
        {
            return await userSettingsStore.SaveAsync(updatedSettings);
        }

        var projectSave = await projectStateStore.SaveAsync(projectDirectory, updatedState);
        if (!projectSave.IsSuccess)
        {
            return projectSave;
        }

        var settingsSave = await userSettingsStore.SaveAsync(updatedSettings);
        if (settingsSave.IsSuccess)
        {
            return settingsSave;
        }

        var rollback = await projectStateStore.SaveAsync(projectDirectory, original.State);
        return rollback.IsSuccess
            ? settingsSave
            : ManifestOperationResult<bool>.Failure(
                $"{settingsSave.Error} Project trust rollback also failed: {rollback.Error}"
            );
    }

    private static LocalProjectTrust GetProjectTrust(UserSettings settings, string projectKey)
    {
        if (!settings.Projects.TryGetValue(projectKey, out var trust))
        {
            trust = new LocalProjectTrust();
            settings.Projects.Add(projectKey, trust);
        }

        return trust;
    }

    private static bool AddProjectSource(List<string> sources, string sourceName) =>
        AddUnique(sources, sourceName);

    private static bool AddProjectPack(
        List<ProjectConfiguration.TrustedPack> packs,
        string sourceName,
        string packId
    )
    {
        if (
            packs.Exists(pack =>
                string.Equals(pack.Source, sourceName, StringComparison.Ordinal)
                && string.Equals(pack.Id, packId, StringComparison.Ordinal)
            )
        )
        {
            return false;
        }

        packs.Add(new ProjectConfiguration.TrustedPack { Id = packId, Source = sourceName });
        return true;
    }

    private static bool RemoveProjectPack(
        List<ProjectConfiguration.TrustedPack> packs,
        string sourceName,
        string packId
    ) =>
        packs.RemoveAll(pack =>
            string.Equals(pack.Source, sourceName, StringComparison.Ordinal)
            && string.Equals(pack.Id, packId, StringComparison.Ordinal)
        ) > 0;

    private static bool RemovePack(
        List<TrustedPackIdentity>? packs,
        ConfiguredSourceIdentity source,
        string packId
    ) =>
        packs?.RemoveAll(pack =>
            pack.Source == source && string.Equals(pack.Id, packId, StringComparison.Ordinal)
        ) > 0;

    private static bool AddUnique<T>(List<T> values, T value)
    {
        if (values.Contains(value))
        {
            return false;
        }

        values.Add(value);
        return true;
    }

    private static ProjectState CloneState(ProjectState state) =>
        state with
        {
            Configuration = state.Configuration with
            {
                Trust = new ProjectConfiguration.ProjectTrust
                {
                    Sources = [.. state.Configuration.Trust.Sources],
                    Packs = [.. state.Configuration.Trust.Packs],
                },
            },
        };

    private static UserSettings CloneSettings(UserSettings settings) =>
        new()
        {
            Global = CloneTrust(settings.Global),
            Projects = settings.Projects.ToDictionary(
                entry => entry.Key,
                entry => new LocalProjectTrust
                {
                    Sources = [.. entry.Value.Sources],
                    Packs = [.. entry.Value.Packs],
                    Acknowledgements = CloneTrust(entry.Value.Acknowledgements),
                },
                StringComparer.Ordinal
            ),
        };

    private static UserTrust CloneTrust(UserTrust trust) =>
        new() { Sources = [.. trust.Sources], Packs = [.. trust.Packs] };

    private static string CreateWarning(
        TrustScope scope,
        IReadOnlyList<KeyValuePair<string, ConfiguredSourceIdentity>> sources,
        IReadOnlyList<string>? packIds
    )
    {
        var warning = new StringBuilder(
            "DANGER: Trusted lifecycle scripts run with your permissions. They may exploit repository or source compromise, future pack versions, credentials, filesystem and network access, dependencies, or irreversible external side effects."
        );
        warning.AppendLine().Append("Scope: ").Append(FormatScope(scope));
        foreach (var source in sources)
        {
            warning
                .AppendLine()
                .Append("Source ")
                .Append(Escape(source.Key))
                .Append(": ")
                .Append(FormatIdentity(source.Value));
        }

        if (packIds is not null)
        {
            warning.AppendLine().Append("Packs: ").AppendJoin(", ", packIds.Select(Escape));
        }

        return warning.ToString();
    }

    private static string FormatScope(TrustScope scope) =>
        scope switch
        {
            TrustScope.LocalUser => "local user for this canonical project",
            TrustScope.Project => "project declaration with local-user acknowledgement",
            TrustScope.GlobalUser => "global user across all projects",
            _ => throw new ArgumentOutOfRangeException(nameof(scope)),
        };

    private static string FormatIdentity(ConfiguredSourceIdentity identity) =>
        identity.Type switch
        {
            "local" => $"local(path={Escape(identity.Path)})",
            "git" =>
                $"git(url={Escape(identity.Url)}, ref={Escape(identity.Ref ?? "<default>")}, path={Escape(identity.Path ?? "<root>")})",
            _ => throw new ArgumentOutOfRangeException(nameof(identity)),
        };

    private static string Escape(string? value) =>
        (value ?? string.Empty)
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\r", "\\r", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal);
}
