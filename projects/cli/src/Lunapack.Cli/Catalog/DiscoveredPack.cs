using Lunapack.Cli.Packs.Manifest;
using Lunapack.Cli.Sources;

namespace Lunapack.Cli.Catalog;

internal sealed record DiscoveredPack(
    string SourcePath,
    string PackDirectory,
    PackManifest Manifest,
    string SourceName,
    ConfiguredSourceIdentity SourceIdentity,
    GitSourceProvenance? GitSource = null,
    string? RepositoryPath = null
)
{
    public PackSourceSelection? SourceSelection { get; init; }

    public DiscoveredPack(
        string sourcePath,
        string packDirectory,
        PackManifest manifest,
        GitSourceProvenance? gitSource = null,
        string? repositoryPath = null
    )
        : this(
            sourcePath,
            packDirectory,
            manifest,
            "source",
            CreateIdentity(sourcePath, gitSource),
            gitSource,
            repositoryPath
        ) { }

    private static ConfiguredSourceIdentity CreateIdentity(
        string sourcePath,
        GitSourceProvenance? gitSource
    ) =>
        gitSource is null
            ? ConfiguredSourceIdentity.CreateLocal(sourcePath)
            : ConfiguredSourceIdentity.CreateGit(gitSource.Url, gitSource.Ref, gitSource.Path);
}
