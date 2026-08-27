using System.IO.Abstractions;

namespace Lunapack.Cli;

internal sealed class GitPackMaterializer(
    IFileSystem fileSystem,
    IGitProcessRunner processRunner,
    IOperationSnapshotSecurity? snapshotSecurity = null
)
{
    private const int DefaultTimeoutSeconds = 300;
    private readonly IOperationSnapshotSecurity _snapshotSecurity =
        snapshotSecurity ?? new OperationSnapshotSecurity();

    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Maintainability",
        "MA0051:Method is too long",
        Justification = "Materialization owns the workspace lifecycle and snapshot cleanup boundary."
    )]
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
            fileSystem.Directory.CreateDirectory(workspace);
            _snapshotSecurity.ApplyDirectory(workspace);
            var snapshotter = new OperationPackSnapshotter(fileSystem, _snapshotSecurity);
            var materializedPacks = new List<DiscoveredPack>(graph.Packs.Count);
            foreach (var pack in graph.Packs)
            {
                var sourcePack = pack;
                if (pack.GitSource is { } gitSource)
                {
                    var configuredSource = FindConfiguredSource(configuration, gitSource);
                    if (configuredSource is null)
                    {
                        return Failure(
                            workspace,
                            $"Git source '{gitSource.Url}' is not configured for pack '{pack.Manifest.Id}'."
                        );
                    }

                    var gitWorkspace = fileSystem.Path.Combine(
                        workspace,
                        "git",
                        materializedPacks.Count.ToString(
                            System.Globalization.CultureInfo.InvariantCulture
                        )
                    );
                    var materialized = await MaterializePackAsync(
                        pack,
                        configuredSource,
                        gitWorkspace,
                        cancellationToken
                    );
                    if (materialized.Value is not { } materializedPack)
                    {
                        return Failure(
                            workspace,
                            materialized.Error ?? "Unable to materialize Git pack."
                        );
                    }

                    sourcePack = materializedPack;
                }

                var snapshotRoot = fileSystem.Path.Combine(
                    workspace,
                    "snapshots",
                    materializedPacks.Count.ToString(
                        System.Globalization.CultureInfo.InvariantCulture
                    )
                );
                var snapshot = snapshotter.Snapshot(sourcePack, snapshotRoot);
                if (snapshot.Value is not { } snapshottedPack)
                {
                    return Failure(
                        workspace,
                        snapshot.Error ?? "Unable to snapshot resolved pack."
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
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return Failure(
                workspace,
                $"Unable to create Git materialization workspace: {exception.Message}"
            );
        }
    }

    private async Task<ManifestOperationResult<DiscoveredPack>> MaterializePackAsync(
        DiscoveredPack pack,
        ProjectConfiguration.GitSource source,
        string workspace,
        CancellationToken cancellationToken
    )
    {
        var gitSource = pack.GitSource!;
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
