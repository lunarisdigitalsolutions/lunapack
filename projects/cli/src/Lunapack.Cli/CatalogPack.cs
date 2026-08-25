using NuGet.Versioning;

namespace Lunapack.Cli;

internal sealed record CatalogPack(
    string SourcePath,
    string PackDirectory,
    int SourceOrder,
    PackManifest Manifest,
    NuGetVersion Version,
    string SourceName,
    ConfiguredSourceIdentity SourceIdentity,
    GitSourceProvenance? GitSource = null,
    string? RepositoryPath = null
)
{
    public CatalogPack(
        string sourcePath,
        string packDirectory,
        int sourceOrder,
        PackManifest manifest,
        NuGetVersion version,
        GitSourceProvenance? gitSource = null,
        string? repositoryPath = null
    )
        : this(
            sourcePath,
            packDirectory,
            sourceOrder,
            manifest,
            version,
            $"source-{sourceOrder}",
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
