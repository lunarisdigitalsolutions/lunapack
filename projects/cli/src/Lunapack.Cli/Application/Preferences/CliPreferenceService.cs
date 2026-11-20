using System.Globalization;
using System.IO.Abstractions;
using Lunapack.Cli.Application.CommandExecution;
using Lunapack.Cli.Project;
using Lunapack.Cli.Trust;

namespace Lunapack.Cli.Application.Preferences;

internal sealed class CliPreferenceService(
    IFileSystem fileSystem,
    ProjectStateStore projectStateStore,
    UserSettingsStore userSettingsStore
)
{
    public async Task<ManifestOperationResult<EffectivePreferences>> ResolveAsync(
        string projectDirectory
    )
    {
        var localPreferences = await LoadLocalPreferencesAsync(projectDirectory);
        if (!localPreferences.IsSuccess)
        {
            return ManifestOperationResult<EffectivePreferences>.Failure(localPreferences.Error!);
        }

        var userSettings = await userSettingsStore.LoadAsync();
        if (userSettings.Value is not { } settings)
        {
            return ManifestOperationResult<EffectivePreferences>.Failure(userSettings.Error!);
        }

        var projectKey = userSettingsStore.GetProjectKey(projectDirectory);
        if (projectKey.Value is not { } key)
        {
            return ManifestOperationResult<EffectivePreferences>.Failure(projectKey.Error!);
        }

        settings.Projects.TryGetValue(key, out var userProjectSettings);
        var local = localPreferences.Value;
        var userProject = userProjectSettings?.Preferences;
        var global = settings.Global.Preferences;
        return ManifestOperationResult<EffectivePreferences>.Success(
            new EffectivePreferences(
                ResolveValue(
                    local?.Discover?.OnlyComposites,
                    userProject?.Discover?.OnlyComposites,
                    global?.Discover?.OnlyComposites,
                    defaultValue: false
                ),
                ResolveValue(
                    local?.Search?.OnlyComposites,
                    userProject?.Search?.OnlyComposites,
                    global?.Search?.OnlyComposites,
                    defaultValue: false
                ),
                ResolveValue(
                    local?.NextSteps?.Enabled,
                    userProject?.NextSteps?.Enabled,
                    global?.NextSteps?.Enabled,
                    defaultValue: true
                ),
                ResourceLimits.Resolve(
                    local?.ResourceLimits,
                    userProject?.ResourceLimits,
                    global?.ResourceLimits
                ),
                ResolveValue(
                    local?.Git?.AllowInsecureTransport,
                    userProject?.Git?.AllowInsecureTransport,
                    global?.Git?.AllowInsecureTransport,
                    defaultValue: false
                )
            )
        );
    }

    public async Task<
        ManifestOperationResult<IReadOnlyList<KeyValuePair<string, string>>>
    > ListAsync(string projectDirectory, Scope scope)
    {
        var preferences = await LoadPreferencesAsync(projectDirectory, scope);
        return preferences.IsSuccess
            ? ManifestOperationResult<IReadOnlyList<KeyValuePair<string, string>>>.Success(
                GetConfiguredValues(preferences.Value)
            )
            : ManifestOperationResult<IReadOnlyList<KeyValuePair<string, string>>>.Failure(
                preferences.Error!
            );
    }

    public Task<ManifestOperationResult<bool>> SetAsync(
        string projectDirectory,
        string key,
        string value,
        Scope scope
    )
    {
        if (TryParseKey(key, out var preferenceKey))
        {
            if (!bool.TryParse(value, out var parsedPreferenceValue))
            {
                return Task.FromResult(
                    ManifestOperationResult<bool>.Failure(
                        $"Preference '{key}' must be 'true' or 'false'."
                    )
                );
            }

            return UpdateAsync(projectDirectory, preferenceKey, parsedPreferenceValue, scope);
        }

        if (!ResourceLimits.TryParseKey(key, out var resourceLimitKey))
        {
            return Task.FromResult(
                ManifestOperationResult<bool>.Failure($"Unknown preference key '{key}'.")
            );
        }

        var definition = ResourceLimits.GetDefinition(resourceLimitKey);
        if (
            !long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var limit)
            || limit <= 0
            || limit > definition.MaximumValue
        )
        {
            return Task.FromResult(
                ManifestOperationResult<bool>.Failure(
                    $"Resource limit '{key}' must be a positive integer no greater than {definition.MaximumValue.ToString(CultureInfo.InvariantCulture)} {definition.Unit}."
                )
            );
        }

        if (scope == Scope.Local && limit > definition.DefaultValue)
        {
            return Task.FromResult(
                ManifestOperationResult<bool>.Failure(
                    $"Project resource limit '{key}' cannot exceed the built-in default of {definition.DefaultValue.ToString(CultureInfo.InvariantCulture)} {definition.Unit}."
                )
            );
        }

        return UpdateResourceLimitAsync(projectDirectory, resourceLimitKey, limit, scope);
    }

    public Task<ManifestOperationResult<bool>> RemoveAsync(
        string projectDirectory,
        string key,
        Scope scope
    )
    {
        if (TryParseKey(key, out var preferenceKey))
        {
            return UpdateAsync(projectDirectory, preferenceKey, null, scope);
        }

        return ResourceLimits.TryParseKey(key, out var resourceLimitKey)
            ? UpdateResourceLimitAsync(projectDirectory, resourceLimitKey, null, scope)
            : Task.FromResult(
                ManifestOperationResult<bool>.Failure($"Unknown preference key '{key}'.")
            );
    }

    private async Task<ManifestOperationResult<CliPreferences?>> LoadLocalPreferencesAsync(
        string projectDirectory
    )
    {
        var configurationPath = fileSystem.Path.Combine(
            projectDirectory,
            ProjectStateStore.ConfigurationFileName
        );
        if (!fileSystem.File.Exists(configurationPath))
        {
            return ManifestOperationResult<CliPreferences?>.Success(null);
        }

        var state = await projectStateStore.LoadAsync(projectDirectory);
        return state.Value is { } projectState
            ? ManifestOperationResult<CliPreferences?>.Success(
                projectState.Configuration.Preferences
            )
            : ManifestOperationResult<CliPreferences?>.Failure(
                state.Error ?? "Unable to load project preferences."
            );
    }

    private async Task<ManifestOperationResult<CliPreferences?>> LoadPreferencesAsync(
        string projectDirectory,
        Scope scope
    )
    {
        if (scope == Scope.Local)
        {
            return await LoadLocalPreferencesAsync(projectDirectory);
        }

        var settings = await userSettingsStore.LoadAsync();
        if (settings.Value is not { } userSettings)
        {
            return ManifestOperationResult<CliPreferences?>.Failure(settings.Error!);
        }

        if (scope == Scope.Global)
        {
            return ManifestOperationResult<CliPreferences?>.Success(
                userSettings.Global.Preferences
            );
        }

        var projectKey = userSettingsStore.GetProjectKey(projectDirectory);
        if (projectKey.Value is not { } key)
        {
            return ManifestOperationResult<CliPreferences?>.Failure(projectKey.Error!);
        }

        return ManifestOperationResult<CliPreferences?>.Success(
            userSettings.Projects.TryGetValue(key, out var projectSettings)
                ? projectSettings.Preferences
                : null
        );
    }

    private async Task<ManifestOperationResult<bool>> UpdateAsync(
        string projectDirectory,
        PreferenceKey preferenceKey,
        bool? value,
        Scope scope
    )
    {
        if (scope == Scope.Local)
        {
            var state = await projectStateStore.LoadAsync(projectDirectory);
            if (state.Value is not { } projectState)
            {
                return ManifestOperationResult<bool>.Failure(state.Error!);
            }

            var currentPreferences = projectState.Configuration.Preferences;
            if (value is null && GetValue(currentPreferences, preferenceKey) is null)
            {
                return MissingValueFailure(GetPreferenceKeyName(preferenceKey));
            }

            var saved = await projectStateStore.SaveAsync(
                projectDirectory,
                projectState with
                {
                    Configuration = projectState.Configuration with
                    {
                        Preferences = UpdatePreferences(currentPreferences, preferenceKey, value),
                    },
                }
            );
            return saved;
        }

        var settings = await userSettingsStore.LoadAsync();
        if (settings.Value is not { } userSettings)
        {
            return ManifestOperationResult<bool>.Failure(settings.Error!);
        }

        if (scope == Scope.Global)
        {
            var currentPreferences = userSettings.Global.Preferences;
            if (value is null && GetValue(currentPreferences, preferenceKey) is null)
            {
                return MissingValueFailure(GetPreferenceKeyName(preferenceKey));
            }

            userSettings.Global.Preferences = UpdatePreferences(
                currentPreferences,
                preferenceKey,
                value
            );
        }
        else
        {
            var projectKey = userSettingsStore.GetProjectKey(projectDirectory);
            if (projectKey.Value is not { } key)
            {
                return ManifestOperationResult<bool>.Failure(projectKey.Error!);
            }

            userSettings.Projects.TryGetValue(key, out var projectSettings);
            var currentPreferences = projectSettings?.Preferences;
            if (value is null && GetValue(currentPreferences, preferenceKey) is null)
            {
                return MissingValueFailure(GetPreferenceKeyName(preferenceKey));
            }

            projectSettings ??= new LocalProjectTrust();
            projectSettings.Preferences = UpdatePreferences(
                currentPreferences,
                preferenceKey,
                value
            );
            userSettings.Projects[key] = projectSettings;
        }

        return await userSettingsStore.SaveAsync(userSettings);
    }

    private async Task<ManifestOperationResult<bool>> UpdateResourceLimitAsync(
        string projectDirectory,
        ResourceLimits.ResourceLimitKey resourceLimitKey,
        long? value,
        Scope scope
    )
    {
        var key = ResourceLimits.GetDefinition(resourceLimitKey).Name;
        if (scope == Scope.Local)
        {
            var state = await projectStateStore.LoadAsync(projectDirectory);
            if (state.Value is not { } projectState)
            {
                return ManifestOperationResult<bool>.Failure(state.Error!);
            }

            var preferences = projectState.Configuration.Preferences;
            if (value is null && preferences?.ResourceLimits?.GetValue(resourceLimitKey) is null)
            {
                return MissingValueFailure(key);
            }

            return await projectStateStore.SaveAsync(
                projectDirectory,
                projectState with
                {
                    Configuration = projectState.Configuration with
                    {
                        Preferences = UpdateResourceLimitPreferences(
                            preferences,
                            resourceLimitKey,
                            value
                        ),
                    },
                }
            );
        }

        var settings = await userSettingsStore.LoadAsync();
        if (settings.Value is not { } userSettings)
        {
            return ManifestOperationResult<bool>.Failure(settings.Error!);
        }

        if (scope == Scope.Global)
        {
            var preferences = userSettings.Global.Preferences;
            if (value is null && preferences?.ResourceLimits?.GetValue(resourceLimitKey) is null)
            {
                return MissingValueFailure(key);
            }

            userSettings.Global.Preferences = UpdateResourceLimitPreferences(
                preferences,
                resourceLimitKey,
                value
            );
        }
        else
        {
            var projectKey = userSettingsStore.GetProjectKey(projectDirectory);
            if (projectKey.Value is not { } keyValue)
            {
                return ManifestOperationResult<bool>.Failure(projectKey.Error!);
            }

            userSettings.Projects.TryGetValue(keyValue, out var projectSettings);
            var preferences = projectSettings?.Preferences;
            if (value is null && preferences?.ResourceLimits?.GetValue(resourceLimitKey) is null)
            {
                return MissingValueFailure(key);
            }

            projectSettings ??= new LocalProjectTrust();
            projectSettings.Preferences = UpdateResourceLimitPreferences(
                preferences,
                resourceLimitKey,
                value
            );
            userSettings.Projects[keyValue] = projectSettings;
        }

        return await userSettingsStore.SaveAsync(userSettings);
    }

    private static ManifestOperationResult<bool> MissingValueFailure(string key) =>
        ManifestOperationResult<bool>.Failure(
            $"Preference '{key}' is not set in the selected scope."
        );

    private static CliPreferences? UpdatePreferences(
        CliPreferences? preferences,
        PreferenceKey key,
        bool? value
    )
    {
        var updated = preferences ?? new CliPreferences();
        updated = key switch
        {
            PreferenceKey.DiscoverOnlyComposites => updated with
            {
                Discover = UpdateCatalogPreferences(value),
            },
            PreferenceKey.SearchOnlyComposites => updated with
            {
                Search = UpdateCatalogPreferences(value),
            },
            PreferenceKey.NextStepsEnabled => updated with
            {
                NextSteps = value is { } enabled
                    ? new NextStepPreferences { Enabled = enabled }
                    : null,
            },
            PreferenceKey.GitAllowInsecureTransport => updated with
            {
                Git = value is { } allowInsecureTransport
                    ? new GitPreferences { AllowInsecureTransport = allowInsecureTransport }
                    : null,
            },
            _ => throw new ArgumentOutOfRangeException(nameof(key), key, null),
        };
        return
            updated.Discover is null
            && updated.Git is null
            && updated.Search is null
            && updated.NextSteps is null
            && updated.ResourceLimits is null
            ? null
            : updated;
    }

    private static CliPreferences? UpdateResourceLimitPreferences(
        CliPreferences? preferences,
        ResourceLimits.ResourceLimitKey key,
        long? value
    )
    {
        var updated = (preferences ?? new CliPreferences()) with
        {
            ResourceLimits = (
                preferences?.ResourceLimits ?? new ResourceLimitPreferences()
            ).WithValue(key, value),
        };
        return
            updated.Discover is null
            && updated.Git is null
            && updated.Search is null
            && updated.NextSteps is null
            && updated.ResourceLimits is null
            ? null
            : updated;
    }

    private static CatalogPreferences? UpdateCatalogPreferences(bool? value) =>
        value is { } onlyComposites
            ? new CatalogPreferences { OnlyComposites = onlyComposites }
            : null;

    private static List<KeyValuePair<string, string>> GetConfiguredValues(
        CliPreferences? preferences
    )
    {
        var values = new List<KeyValuePair<string, string>>();
        AddValue(values, "discover.only-composites", preferences?.Discover?.OnlyComposites);
        AddValue(values, "git.allow-insecure-transport", preferences?.Git?.AllowInsecureTransport);
        AddValue(values, "next-steps.enabled", preferences?.NextSteps?.Enabled);
        AddValue(values, "search.only-composites", preferences?.Search?.OnlyComposites);
        foreach (var definition in ResourceLimits.Definitions)
        {
            if (preferences?.ResourceLimits?.GetValue(definition.Key) is { } value)
            {
                values.Add(
                    new KeyValuePair<string, string>(
                        definition.Name,
                        value.ToString(CultureInfo.InvariantCulture)
                    )
                );
            }
        }

        return values;
    }

    private static void AddValue(List<KeyValuePair<string, string>> values, string key, bool? value)
    {
        if (value is { } configuredValue)
        {
            values.Add(
                new KeyValuePair<string, string>(key, configuredValue.ToString().ToLowerInvariant())
            );
        }
    }

    private static bool? GetValue(CliPreferences? preferences, PreferenceKey key) =>
        key switch
        {
            PreferenceKey.DiscoverOnlyComposites => preferences?.Discover?.OnlyComposites,
            PreferenceKey.SearchOnlyComposites => preferences?.Search?.OnlyComposites,
            PreferenceKey.NextStepsEnabled => preferences?.NextSteps?.Enabled,
            PreferenceKey.GitAllowInsecureTransport => preferences?.Git?.AllowInsecureTransport,
            _ => throw new ArgumentOutOfRangeException(nameof(key), key, null),
        };

    private static bool ResolveValue(
        bool? local,
        bool? userProject,
        bool? global,
        bool defaultValue
    ) => local ?? userProject ?? global ?? defaultValue;

    private static bool TryParseKey(string key, out PreferenceKey preferenceKey)
    {
        preferenceKey = key switch
        {
            "discover.only-composites" => PreferenceKey.DiscoverOnlyComposites,
            "search.only-composites" => PreferenceKey.SearchOnlyComposites,
            "next-steps.enabled" => PreferenceKey.NextStepsEnabled,
            "git.allow-insecure-transport" => PreferenceKey.GitAllowInsecureTransport,
            _ => (PreferenceKey)(-1),
        };
        return Enum.IsDefined(preferenceKey);
    }

    private static string GetPreferenceKeyName(PreferenceKey key) =>
        key switch
        {
            PreferenceKey.DiscoverOnlyComposites => "discover.only-composites",
            PreferenceKey.SearchOnlyComposites => "search.only-composites",
            PreferenceKey.NextStepsEnabled => "next-steps.enabled",
            PreferenceKey.GitAllowInsecureTransport => "git.allow-insecure-transport",
            _ => throw new ArgumentOutOfRangeException(nameof(key), key, null),
        };

    internal enum Scope
    {
        Local,
        UserProject,
        Global,
    }

    internal enum PreferenceKey
    {
        DiscoverOnlyComposites,
        SearchOnlyComposites,
        NextStepsEnabled,
        GitAllowInsecureTransport,
    }

    internal sealed record EffectivePreferences(
        bool DiscoverOnlyComposites,
        bool SearchOnlyComposites,
        bool NextStepsEnabled,
        ResourceLimits ResourceLimits,
        bool AllowInsecureGitTransport
    );
}
