using Lunapack.Cli.Sources;
using Lunapack.Cli.Sources.Git;

namespace Lunapack.Cli.Links;

internal sealed record LinkSourceListing(
    ConfiguredSourceIdentity Identity,
    GitSourceProvenance? GitSource,
    string RootDirectory,
    IReadOnlyList<string> Paths
) : IDisposable
{
    public IReadOnlyDictionary<string, string> BlobIds { get; init; } =
        new Dictionary<string, string>(StringComparer.Ordinal);

    public GitTransportMode TransportMode { get; init; } = GitTransportMode.SecureOnly;

    public string SourceName { get; init; } = string.Empty;

    public Action? Cleanup { get; init; }

    public void Dispose() => Cleanup?.Invoke();
}
