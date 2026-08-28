namespace Lunapack.Cli;

internal static class ScriptDenialOriginFormatter
{
    public static string Format(ScriptDenialOrigin origin) =>
        origin switch
        {
            ScriptDenialOrigin.Project => "project",
            ScriptDenialOrigin.LocalUser => "local-user",
            ScriptDenialOrigin.GlobalUser => "global-user",
            _ => throw new ArgumentOutOfRangeException(nameof(origin)),
        };
}
