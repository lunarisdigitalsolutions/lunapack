namespace Lunapack.Cli;

internal static class SourceOutputFormatter
{
    public static string Format(ProjectConfiguration.Source source) =>
        source switch
        {
            ProjectConfiguration.LocalSource localSource =>
                $"{localSource.Name} - local - path: {localSource.Path} - identity: {FormatIdentity(ConfiguredSourceIdentity.Create(localSource))}",
            ProjectConfiguration.GitSource gitSource => FormatGit(gitSource),
            _ => throw new ArgumentOutOfRangeException(nameof(source)),
        };

    private static string FormatGit(ProjectConfiguration.GitSource source)
    {
        var properties = new List<string> { $"url: {source.Url}" };

        if (source.Ref is not null)
        {
            properties.Add($"ref: {source.Ref}");
        }

        if (source.Path is not null)
        {
            properties.Add($"path: {source.Path}");
        }

        if (source.TimeoutSeconds is { } timeoutSeconds)
        {
            properties.Add($"timeoutSeconds: {timeoutSeconds}");
        }

        properties.Add($"identity: {FormatIdentity(ConfiguredSourceIdentity.Create(source))}");
        return $"{source.Name} - git - {string.Join(" - ", properties)}";
    }

    internal static string FormatIdentity(ConfiguredSourceIdentity identity) =>
        identity.Type switch
        {
            "local" => $"local(path={identity.Path})",
            "git" =>
                $"git(url={identity.Url}, ref={identity.Ref ?? "<default>"}, path={identity.Path ?? "<root>"})",
            _ => throw new ArgumentOutOfRangeException(nameof(identity)),
        };
}
