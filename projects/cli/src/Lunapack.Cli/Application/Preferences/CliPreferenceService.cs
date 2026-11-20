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
                ResolveValue(
                    local?.Git?.AllowInsecureTransport,
                    userProject?.Git?.AllowInsecureTransport,
                    global?.Git?.AllowInsecureTransport,
                    defaultValue: false
                )
            )
        );
    }

    public async Task<ManifestOperationResult<IReadOnlyList<KeyValuePair<string, bool>>>> ListAsync(
        string projectDirectory,
        Scope scope
    )
    {
        var preferences = await LoadPreferencesAsync(projectDirectory, scope);
        return preferences.IsSuccess
            ? ManifestOperationResult<IReadOnlyList<KeyValuePair<string, bool>>>.Success(
                GetConfiguredValues(preferences.Value)
            )
            : ManifestOperationResult<IReadOnlyList<KeyValuePair<string, bool>>>.Failure(
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
        if (!TryParseKey(key, out var preferenceKey))
        {
            return Task.FromResult(
                ManifestOperationResult<bool>.Failure($"Unknown preference key '{key}'.")
            );
        }

        if (!bool.TryParse(value, out var parsedValue))
        {
            return Task.FromResult(
                ManifestOperationResult<bool>.Failure(
                    $"Preference '{key}' must be 'true' or 'false'."
                )
            );
        }

        return UpdateAsync(projectDirectory, preferenceKey, parsedValue, scope);
    }

    public Task<ManifestOperationResult<bool>> RemoveAsync(
        string projectDirectory,
        string key,
        Scope scope
    )
    {
        if (!TryParseKey(key, out var preferenceKey))
        {
            return Task.FromResult(
                ManifestOperationResult<bool>.Failure($"Unknown preference key '{key}'.")
            );
        }

        return UpdateAsync(projectDirectory, preferenceKey, null, scope);
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
                return MissingValueFailure(preferenceKey);
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
                return MissingValueFailure(preferenceKey);
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
                return MissingValueFailure(preferenceKey);
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

    private static ManifestOperationResult<bool> MissingValueFailure(PreferenceKey key) =>
        ManifestOperationResult<bool>.Failure(
            $"Preference '{GetKeyName(key)}' is not set in the selected scope."
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
            ? null
            : updated;
    }

    private static CatalogPreferences? UpdateCatalogPreferences(bool? value) =>
        value is { } onlyComposites
            ? new CatalogPreferences { OnlyComposites = onlyComposites }
            : null;

    private static List<KeyValuePair<string, bool>> GetConfiguredValues(CliPreferences? preferences)
    {
        var values = new List<KeyValuePair<string, bool>>();
        AddValue(values, "discover.only-composites", preferences?.Discover?.OnlyComposites);
        AddValue(values, "git.allow-insecure-transport", preferences?.Git?.AllowInsecureTransport);
        AddValue(values, "next-steps.enabled", preferences?.NextSteps?.Enabled);
        AddValue(values, "search.only-composites", preferences?.Search?.OnlyComposites);
        return values;
    }

    private static void AddValue(List<KeyValuePair<string, bool>> values, string key, bool? value)
    {
        if (value is { } configuredValue)
        {
            values.Add(new KeyValuePair<string, bool>(key, configuredValue));
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

    private static string GetKeyName(PreferenceKey key) =>
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
        bool AllowInsecureGitTransport
    );
}
