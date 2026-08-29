using System.IO.Abstractions;
using Lunapack.Cli.Application.CommandExecution;
using Lunapack.Cli.Catalog;
using Lunapack.Cli.Packs.Planning;
using Lunapack.Cli.Project;

namespace Lunapack.Cli.Sources.Git;

internal sealed class GitPackMaterializer(
    IFileSystem fileSystem,
    IGitProcessRunner processRunner,
    IOperationSnapshotSecurity? snapshotSecurity = null
)
{
    private const int DefaultTimeoutSeconds = 300;
    private readonly IOperationSnapshotSecurity _snapshotSecurity =
        snapshotSecurity ?? new OperationSnapshotSecurity();

    public async Task<ManifestOperationResult<GitPackMaterialization>> MaterializeAsync(
        ResolvedPackGraph graph,
        ProjectConfiguration configuration,
        CancellationToken cancellationToken = default
    )
    {
        var workspace = fileSystem.Path.Combine(
            Path.GetTempPath(),
            "lunapack",
            "operations",
            Guid.NewGuid().ToString("N")
        );
        try
        {
            PrepareWorkspace(workspace);
            return await MaterializeGraphAsync(graph, configuration, workspace, cancellationToken);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return Failure(
                workspace,
                $"Unable to create Git materialization workspace: {exception.Message}"
            );
        }
    }

    private void PrepareWorkspace(string workspace)
    {
        fileSystem.Directory.CreateDirectory(workspace);
        _snapshotSecurity.ApplyDirectory(workspace);
    }

    private async Task<ManifestOperationResult<GitPackMaterialization>> MaterializeGraphAsync(
        ResolvedPackGraph graph,
        ProjectConfiguration configuration,
        string workspace,
        CancellationToken cancellationToken
    )
    {
        var snapshotter = new OperationPackSnapshotter(fileSystem, _snapshotSecurity);
        var materializedPacks = new List<DiscoveredPack>(graph.Packs.Count);
        foreach (var pack in graph.Packs)
        {
            var sourcePackResult = await MaterializeSourcePackAsync(
                pack,
                configuration,
                workspace,
                materializedPacks.Count,
                cancellationToken
            );
            if (sourcePackResult.Value is not { } sourcePack)
            {
                return Failure(
                    workspace,
                    sourcePackResult.Error ?? "Unable to materialize Git pack."
                );
            }

            var snapshotRoot = fileSystem.Path.Combine(
                workspace,
                "snapshots",
                materializedPacks.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)
            );
            var snapshot = snapshotter.Snapshot(sourcePack, snapshotRoot);
            if (snapshot.Value is not { } snapshottedPack)
            {
                return Failure(workspace, snapshot.Error ?? "Unable to snapshot resolved pack.");
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
        DiscoveredPack pack,
        ProjectConfiguration configuration,
        string workspace,
        int packIndex,
        CancellationToken cancellationToken
    )
    {
        if (pack.GitSource is not { } gitSource)
        {
            return ManifestOperationResult<DiscoveredPack>.Success(pack);
        }

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
            cancellationToken
        );
    }

    private async Task<ManifestOperationResult<DiscoveredPack>> MaterializePackAsync(
        DiscoveredPack pack,
        GitSourceProvenance gitSource,
        ProjectConfiguration.GitSource source,
        string workspace,
        CancellationToken cancellationToken
    )
    {
        var timeout = TimeSpan.FromSeconds(source.TimeoutSeconds ?? DefaultTimeoutSeconds);
        fileSystem.Directory.CreateDirectory(workspace);
        foreach (
            var command in new[]
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
            }
        )
        {
            var result = await processRunner.RunAsync(command, timeout, cancellationToken);
            if (!result.IsSuccess)
            {
                return ManifestOperationResult<DiscoveredPack>.Failure(
                    result.Error ?? "Git pack materialization failed."
                );
            }
        }

        return ManifestOperationResult<DiscoveredPack>.Success(
            pack with
            {
                SourcePath = workspace,
                PackDirectory = GetPackDirectory(workspace, pack.RepositoryPath),
            }
        );
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

    private ManifestOperationResult<GitPackMaterialization> Failure(string workspace, string error)
    {
        _snapshotSecurity.PrepareForDelete(fileSystem, workspace);
        GitTemporaryWorkspace.Delete(fileSystem, workspace);

        return ManifestOperationResult<GitPackMaterialization>.Failure(error);
    }
}
