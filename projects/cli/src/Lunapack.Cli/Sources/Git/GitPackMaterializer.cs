using System.IO.Abstractions;
using System.Text.Json;
using System.Text.Json.Nodes;
using Lunapack.Cli.Application;
using Lunapack.Cli.Application.CommandExecution;
using Lunapack.Cli.Application.Preferences;
using Lunapack.Cli.Application.Serialization;
using Lunapack.Cli.Catalog;
using Lunapack.Cli.Packs.Manifest;
using Lunapack.Cli.Packs.Planning;
using Lunapack.Cli.Project;
using Lunapack.Cli.Sources;
using Lunapack.Cli.Sources.Oci;

namespace Lunapack.Cli.Sources.Git;

internal sealed class GitPackMaterializer(
    IFileSystem fileSystem,
    IGitProcessRunner processRunner,
    CliConsole console,
    IOperationSnapshotSecurity? snapshotSecurity = null,
    OciPackSource? ociPackSource = null,
    PackManifestStore? packManifestStore = null
)
{
    private const int DefaultTimeoutSeconds = 300;
    private readonly IOperationSnapshotSecurity _snapshotSecurity =
        snapshotSecurity ?? new OperationSnapshotSecurity();
    private readonly OciPackSource? _ociPackSource = ociPackSource;
    private readonly PackManifestStore _packManifestStore = packManifestStore ?? new(fileSystem);

    public async Task<ManifestOperationResult<GitPackMaterialization>> MaterializeAsync(
        string projectDirectory,
        ResolvedPackGraph graph,
        ProjectConfiguration configuration,
        ResourceLimits? resourceLimits = null,
        CancellationToken cancellationToken = default
    )
    {
        var effectiveResourceLimits = resourceLimits ?? ResourceLimits.Default;
        var workspace = fileSystem.Path.Combine(
            Path.GetTempPath(),
            "lunapack",
            "operations",
            Guid.NewGuid().ToString("N")
        );
        try
        {
            PrepareWorkspace(workspace);
            return await MaterializeGraphAsync(
                projectDirectory,
                graph,
                configuration,
                workspace,
                effectiveResourceLimits,
                cancellationToken
            );
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return Failure(
                workspace,
                $"Unable to create Git materialization workspace: {exception.Message}"
            );
        }
        catch (OperationCanceledException)
        {
            PrepareForDelete(workspace);
            throw;
        }
    }

    private void PrepareWorkspace(string workspace)
    {
        fileSystem.Directory.CreateDirectory(workspace);
        _snapshotSecurity.ApplyDirectory(workspace);
    }

    private async Task<ManifestOperationResult<GitPackMaterialization>> MaterializeGraphAsync(
        string projectDirectory,
        ResolvedPackGraph graph,
        ProjectConfiguration configuration,
        string workspace,
        ResourceLimits resourceLimits,
        CancellationToken cancellationToken
    )
    {
        var snapshotter = new OperationPackSnapshotter(fileSystem, _snapshotSecurity, console);
        var materializedPacks = new List<DiscoveredPack>(graph.Packs.Count);
        foreach (var pack in graph.Packs)
        {
            var sourcePackResult = await MaterializeSourcePackAsync(
                projectDirectory,
                pack,
                configuration,
                workspace,
                materializedPacks.Count,
                resourceLimits,
                cancellationToken
            );
            if (sourcePackResult.Value is not { } sourcePack)
            {
                return Failure(
                    workspace,
                    sourcePackResult.Error ?? "Unable to materialize Git pack.",
                    sourcePackResult.ErrorKind
                );
            }

            var snapshotRoot = fileSystem.Path.Combine(
                workspace,
                "snapshots",
                materializedPacks.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)
            );
            var snapshot = snapshotter.Snapshot(sourcePack, snapshotRoot, resourceLimits);
            if (snapshot.Value is not { } snapshottedPack)
            {
                return Failure(
                    workspace,
                    snapshot.Error ?? "Unable to snapshot resolved pack.",
                    snapshot.ErrorKind
                );
            }

            materializedPacks.Add(snapshottedPack);
        }

        return ManifestOperationResult<GitPackMaterialization>.Success(
            new GitPackMaterialization(
                fileSystem,
                new ResolvedPackGraph(materializedPacks, graph.RootPackIds),
                workspace,
                _snapshotSecurity
            )
        );
    }

    private async Task<ManifestOperationResult<DiscoveredPack>> MaterializeSourcePackAsync(
        string projectDirectory,
        DiscoveredPack pack,
        ProjectConfiguration configuration,
        string workspace,
        int packIndex,
        ResourceLimits resourceLimits,
        CancellationToken cancellationToken
    )
    {
        if (pack.GitSource is { } gitSource)
        {
            var configuredSource = FindConfiguredSource(configuration, gitSource);
            if (configuredSource is null)
            {
                return ManifestOperationResult<DiscoveredPack>.Failure(
                    $"Git source '{gitSource.Url}' is not configured for pack '{pack.Manifest.Id}'."
                );
            }

            var gitWorkspace = fileSystem.Path.Combine(
                workspace,
                "git",
                packIndex.ToString(System.Globalization.CultureInfo.InvariantCulture)
            );
            return await MaterializePackAsync(
                pack,
                gitSource,
                configuredSource,
                gitWorkspace,
                resourceLimits,
                cancellationToken
            );
        }

        if (pack.OciSource is { } ociProvenance)
        {
            return await MaterializeOciPackAsync(
                projectDirectory,
                pack,
                ociProvenance,
                configuration,
                workspace,
                packIndex,
                resourceLimits,
                cancellationToken
            );
        }

        return ManifestOperationResult<DiscoveredPack>.Success(pack);
    }

    private async Task<ManifestOperationResult<DiscoveredPack>> MaterializeOciPackAsync(
        string projectDirectory,
        DiscoveredPack pack,
        OciSourceProvenance provenance,
        ProjectConfiguration configuration,
        string workspace,
        int packIndex,
        ResourceLimits resourceLimits,
        CancellationToken cancellationToken
    )
    {
        if (_ociPackSource is null)
        {
            return ManifestOperationResult<DiscoveredPack>.Failure(
                $"OCI source materialization is unavailable for pack '{pack.Manifest.Id}'."
            );
        }

        var source = FindConfiguredOciSource(configuration, pack);
        if (source is null)
        {
            return ManifestOperationResult<DiscoveredPack>.Failure(
                $"OCI source for pack '{pack.Manifest.Id}' is not configured."
            );
        }

        var identity = ValidateOciSourceIdentity(source, pack, provenance);
        if (!identity.IsSuccess)
        {
            return ManifestOperationResult<DiscoveredPack>.Failure(
                identity.Error ?? $"OCI source identity changed for pack '{pack.Manifest.Id}'."
            );
        }

        var ociWorkspace = fileSystem.Path.Combine(
            workspace,
            "oci",
            packIndex.ToString(System.Globalization.CultureInfo.InvariantCulture)
        );
        fileSystem.Directory.CreateDirectory(ociWorkspace);
        var release = await _ociPackSource.FetchReleaseAsync(
            projectDirectory,
            source,
            provenance.Repository,
            pack.Manifest.Id,
            provenance.Tag,
            ociWorkspace,
            provenance.ManifestDigest,
            provenance.ContentDigest,
            resourceLimits: resourceLimits,
            cancellationToken: cancellationToken
        );
        if (release.Value is not { } materializedRelease)
        {
            return ManifestOperationResult<DiscoveredPack>.Failure(
                release.Error
                    ?? $"Unable to retrieve locked OCI pack '{pack.Manifest.Id}@{pack.Manifest.Version}'.",
                release.ErrorKind
            );
        }

        if (
            !string.Equals(
                materializedRelease.Manifest.Id,
                pack.Manifest.Id,
                StringComparison.Ordinal
            )
            || !string.Equals(
                materializedRelease.Manifest.Version,
                pack.Manifest.Version,
                StringComparison.Ordinal
            )
        )
        {
            return ManifestOperationResult<DiscoveredPack>.Failure(
                $"OCI release metadata changed for pack '{pack.Manifest.Id}@{pack.Manifest.Version}'."
            );
        }

        return ManifestOperationResult<DiscoveredPack>.Success(
            pack with
            {
                SourcePath = ociWorkspace,
                PackDirectory = materializedRelease.PackDirectory,
                OciSource = materializedRelease.Provenance,
            }
        );
    }

    private static ManifestOperationResult<bool> ValidateOciSourceIdentity(
        ProjectConfiguration.OciSource source,
        DiscoveredPack pack,
        OciSourceProvenance provenance
    )
    {
        var location = SourceIdentityNormalizer.NormalizeOciLocation(source.Url, source.Path);
        var repository = OciRepositoryTagMapper.CreateRepository(source, pack.Manifest.Id);
        var tag = OciRepositoryTagMapper.CreateTag(pack.Manifest.Version);
        var matches =
            location.IsSuccess
            && repository.IsSuccess
            && tag.IsSuccess
            && string.Equals(location.Value.Url, provenance.RegistryUrl, StringComparison.Ordinal)
            && string.Equals(repository.Value, provenance.Repository, StringComparison.Ordinal)
            && string.Equals(tag.Value, provenance.Tag, StringComparison.Ordinal);
        return matches
            ? ManifestOperationResult<bool>.Success(true)
            : ManifestOperationResult<bool>.Failure(
                $"OCI source identity or version changed for pack '{pack.Manifest.Id}'."
            );
    }

    private static ProjectConfiguration.OciSource? FindConfiguredOciSource(
        ProjectConfiguration configuration,
        DiscoveredPack pack
    ) =>
        configuration
            .Sources.OfType<ProjectConfiguration.OciSource>()
            .SingleOrDefault(source =>
                ConfiguredSourceIdentity.Create(source) == pack.SourceIdentity
            );

    private async Task<ManifestOperationResult<DiscoveredPack>> MaterializePackAsync(
        DiscoveredPack pack,
        GitSourceProvenance gitSource,
        ProjectConfiguration.GitSource source,
        string workspace,
        ResourceLimits resourceLimits,
        CancellationToken cancellationToken
    )
    {
        var timeout = TimeSpan.FromSeconds(source.TimeoutSeconds ?? DefaultTimeoutSeconds);
        fileSystem.Directory.CreateDirectory(workspace);
        var commands = new[]
        {
            new[] { "init", "--quiet", workspace },
            ["-C", workspace, "remote", "add", "origin", gitSource.Url],
            [
                "-C",
                workspace,
                "fetch",
                "--depth=1",
                "--filter=blob:none",
                "origin",
                gitSource.ResolvedCommit,
            ],
            ["-C", workspace, "sparse-checkout", "init", "--no-cone"],
            [
                "-C",
                workspace,
                "sparse-checkout",
                "set",
                "--no-cone",
                "--",
                CreateSparsePattern(pack.RepositoryPath),
            ],
            ["-C", workspace, "checkout", "--quiet", "--detach", "FETCH_HEAD"],
        };
        foreach (var command in commands)
        {
            var result = await processRunner.RunAsync(command, timeout, cancellationToken);
            if (!result.IsSuccess)
            {
                return ManifestOperationResult<DiscoveredPack>.Failure(
                    result.Error ?? "Git pack materialization failed."
                );
            }
        }

        var packDirectory = GetPackDirectory(workspace, pack.RepositoryPath);
        var loadedManifest = await _packManifestStore.LoadAsync(
            packDirectory,
            resourceLimits.PackManifestBytes
        );
        if (loadedManifest.Value is not { } checkedOutManifest)
        {
            return ManifestOperationResult<DiscoveredPack>.Failure(
                loadedManifest.Error ?? "Unable to verify materialized Git pack manifest.",
                loadedManifest.ErrorKind
            );
        }

        if (!HaveEquivalentManifest(pack.Manifest, checkedOutManifest))
        {
            return ManifestOperationResult<DiscoveredPack>.Failure(
                $"Materialized pack '{pack.Manifest.Id}@{pack.Manifest.Version}' does not match the manifest resolved from Git commit '{gitSource.ResolvedCommit}'.",
                ManifestOperationErrorKind.SourceIntegrityMismatch
            );
        }

        return ManifestOperationResult<DiscoveredPack>.Success(
            pack with
            {
                SourcePath = workspace,
                PackDirectory = packDirectory,
            }
        );
    }

    private static bool HaveEquivalentManifest(PackManifest expected, PackManifest actual)
    {
        var expectedNode = JsonNode.Parse(
            JsonSerializer.Serialize(expected, LunapackJsonContext.Default.PackManifest)
        );
        var actualNode = JsonNode.Parse(
            JsonSerializer.Serialize(actual, LunapackJsonContext.Default.PackManifest)
        );
        return JsonNode.DeepEquals(expectedNode, actualNode);
    }

    private static ProjectConfiguration.GitSource? FindConfiguredSource(
        ProjectConfiguration configuration,
        GitSourceProvenance gitSource
    ) =>
        configuration
            .Sources.OfType<ProjectConfiguration.GitSource>()
            .SingleOrDefault(source =>
                string.Equals(source.Url, gitSource.Url, StringComparison.Ordinal)
                && string.Equals(source.Ref, gitSource.Ref, StringComparison.Ordinal)
                && string.Equals(source.Path, gitSource.Path, StringComparison.Ordinal)
            );

    private static string CreateSparsePattern(string? repositoryPath) =>
        string.IsNullOrEmpty(repositoryPath)
        || string.Equals(repositoryPath, ".", StringComparison.Ordinal)
            ? "/*"
            : $"/{repositoryPath.Trim('/')}/**";

    private string GetPackDirectory(string workspace, string? repositoryPath) =>
        string.IsNullOrEmpty(repositoryPath)
        || string.Equals(repositoryPath, ".", StringComparison.Ordinal)
            ? workspace
            : fileSystem.Path.Combine([
                workspace,
                .. repositoryPath.Split('/', StringSplitOptions.RemoveEmptyEntries),
            ]);

    private ManifestOperationResult<GitPackMaterialization> Failure(
        string workspace,
        string error,
        ManifestOperationErrorKind errorKind = ManifestOperationErrorKind.General
    )
    {
        PrepareForDelete(workspace);

        return ManifestOperationResult<GitPackMaterialization>.Failure(error, errorKind);
    }

    private void PrepareForDelete(string workspace)
    {
        _snapshotSecurity.PrepareForDelete(fileSystem, workspace);
        GitTemporaryWorkspace.Delete(fileSystem, workspace);
    }
}
