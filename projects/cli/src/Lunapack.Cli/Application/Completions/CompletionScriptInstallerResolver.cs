namespace Lunapack.Cli.Application.Completions;

internal sealed class CompletionScriptInstallerResolver(
    IEnumerable<CompletionScriptInstaller> installers
)
{
    private readonly Dictionary<string, CompletionScriptInstaller> _installers =
        installers.ToDictionary(installer => installer.Shell, StringComparer.Ordinal);

    public CompletionScriptInstaller Resolve(string shell) =>
        _installers.TryGetValue(shell, out var installer)
            ? installer
            : throw new ArgumentOutOfRangeException(nameof(shell), shell, "Unsupported shell.");
}
