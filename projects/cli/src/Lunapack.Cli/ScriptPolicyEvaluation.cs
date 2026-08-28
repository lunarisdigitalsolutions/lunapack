namespace Lunapack.Cli;

internal sealed record ScriptPolicyEvaluation(
    UserSettings Settings,
    string ProjectKey,
    IReadOnlyList<ScriptDenialOrigin> DenyingScopes
)
{
    public bool IsDenied => DenyingScopes.Count > 0;
}
