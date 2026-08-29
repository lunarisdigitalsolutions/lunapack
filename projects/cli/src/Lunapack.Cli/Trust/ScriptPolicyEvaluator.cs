using Lunapack.Cli.Application.CommandExecution;
using Lunapack.Cli.Project;

namespace Lunapack.Cli.Trust;

internal sealed class ScriptPolicyEvaluator(UserSettingsStore userSettingsStore)
{
    public async Task<ManifestOperationResult<ScriptPolicyEvaluation>> EvaluateAsync(
        string projectDirectory,
        ProjectConfiguration configuration
    )
    {
        var settings = await userSettingsStore.LoadAsync();
        if (settings.Value is not { } userSettings)
        {
            return ManifestOperationResult<ScriptPolicyEvaluation>.Failure(
                settings.Error ?? "Unable to load lifecycle trust settings."
            );
        }

        var projectKey = userSettingsStore.GetProjectKey(projectDirectory);
        if (projectKey.Value is not { } key)
        {
            return ManifestOperationResult<ScriptPolicyEvaluation>.Failure(
                projectKey.Error ?? "Unable to resolve lifecycle trust project path."
            );
        }

        userSettings.Projects.TryGetValue(key, out var localTrust);
        var denyingScopes = new List<ScriptDenialOrigin>(3);
        if (configuration.Trust.Deny?.Scripts == true)
        {
            denyingScopes.Add(ScriptDenialOrigin.Project);
        }

        if (localTrust?.Deny?.Scripts == true)
        {
            denyingScopes.Add(ScriptDenialOrigin.LocalUser);
        }

        if (userSettings.Global.Deny?.Scripts == true)
        {
            denyingScopes.Add(ScriptDenialOrigin.GlobalUser);
        }

        return ManifestOperationResult<ScriptPolicyEvaluation>.Success(
            new ScriptPolicyEvaluation(userSettings, key, denyingScopes)
        );
    }
}
