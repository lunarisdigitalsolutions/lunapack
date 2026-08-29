using Lunapack.Cli.Sources;

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

    public Action? Cleanup { get; init; }

    public void Dispose() => Cleanup?.Invoke();
}
