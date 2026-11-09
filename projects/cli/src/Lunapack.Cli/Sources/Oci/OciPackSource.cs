using System.IO.Abstractions;
using Lunapack.Cli.Application;
using Lunapack.Cli.Application.CommandExecution;
using Lunapack.Cli.Catalog;
using Lunapack.Cli.Packs.Authoring;
using Lunapack.Cli.Packs.Manifest;
using Lunapack.Cli.Packs.Publishing;
using Lunapack.Cli.Project;
using Lunapack.Cli.Sources.Git;

namespace Lunapack.Cli.Sources.Oci;

internal sealed class OciPackSource : IDisposable
{
    private readonly OciSourceCredentialCoordinator _credentialCoordinator;
    private readonly OciPackDiscovery _discovery;
    private readonly IOciRegistryClient _registryClient;
    private readonly OciPackReleaseLoader _releaseLoader;

    public OciPackSource(
        IFileSystem fileSystem,
        CliConsole console,
        IOciRegistryClient? registryClient = null,
        ISourceCredentialStore? credentialStore = null,
        IOciCredentialPrompt? credentialPrompt = null,
        IOciAccessTokenProvider? accessTokenProvider = null
    )
    {
        _registryClient =
            registryClient
            ?? new OciDistributionRegistryClient(accessTokenProvider: accessTokenProvider);
        _credentialCoordinator = new OciSourceCredentialCoordinator(
            fileSystem,
            credentialStore ?? new NativeSourceCredentialStore(),
            credentialPrompt ?? new ConsoleOciCredentialPrompt(console),
            console
        );
        _releaseLoader = new OciPackReleaseLoader(
            fileSystem,
            _registryClient,
            _credentialCoordinator,
            new PackManifestStore(fileSystem)
        );
        _discovery = new OciPackDiscovery(
            fileSystem,
            _registryClient,
            _credentialCoordinator,
            _releaseLoader,
            console
        );
    }

    public IPackPublisher CreatePublisher(
        IFileSystem fileSystem,
        PackManifestStore packManifestStore,
        PackAuthoringValidationService authoringValidationService,
        IGitProcessRunner gitProcessRunner
    ) =>
        new OciPackPublisher(
            fileSystem,
            _registryClient,
            _credentialCoordinator,
            packManifestStore,
            authoringValidationService,
            gitProcessRunner
        );

    public Task<ManifestOperationResult<IReadOnlyList<CatalogPack>>> BrowseAsync(
        string projectDirectory,
        ProjectConfiguration.OciSource source,
        int sourceOrder,
        CancellationToken cancellationToken = default
    ) => _discovery.BrowseAsync(projectDirectory, source, sourceOrder, cancellationToken);

    public static ManifestOperationResult<IReadOnlyList<CatalogPack>> BrowseCached(
        ProjectConfiguration.OciSource source
    ) => OciPackDiscovery.BrowseCached(source);

    public Task<ManifestOperationResult<IReadOnlyList<CatalogPack>>> ResolveCandidatesAsync(
        string projectDirectory,
        ProjectConfiguration.OciSource source,
        int sourceOrder,
        string packId,
        string? requestedVersion,
        CancellationToken cancellationToken = default
    ) =>
        _discovery.ResolveCandidatesAsync(
            projectDirectory,
            source,
            sourceOrder,
            packId,
            requestedVersion,
            cancellationToken
        );

    public Task<ManifestOperationResult<OciPackRelease?>> FetchReleaseAsync(
        string projectDirectory,
        ProjectConfiguration.OciSource source,
        string repository,
        string repositoryPackId,
        string tag,
        string workspace,
        string? expectedManifestDigest = null,
        string? expectedContentDigest = null,
        CancellationToken cancellationToken = default
    ) =>
        _releaseLoader.FetchAsync(
            projectDirectory,
            source,
            repository,
            repositoryPackId,
            tag,
            workspace,
            expectedManifestDigest,
            expectedContentDigest,
            cancellationToken
        );

    public void Dispose() => _credentialCoordinator.Dispose();
}
