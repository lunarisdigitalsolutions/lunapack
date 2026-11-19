namespace Lunapack.Cli.Sources.Git;

internal sealed record GitSecurityProbeRequest(
    IReadOnlyList<string> Arguments,
    TimeSpan Timeout,
    GitTransportMode TransportMode,
    string RepositoryUrl,
    string? StandardInput = null,
    string? GitIndexPath = null,
    int? MaximumOutputCharacters = null
);
