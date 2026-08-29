using System.Globalization;
using System.IO.Abstractions;
using Lunapack.Cli.Application.CommandExecution;
using Lunapack.Cli.Application.Paths;
using Lunapack.Cli.Packs.Planning;
using Lunapack.Cli.Sources.Git;

namespace Lunapack.Cli.Packs.ExternalSources;

internal sealed class ExternalSourceMaterializer(
    IFileSystem fileSystem,
    IGitProcessRunner processRunner,
    GitRefResolver gitRefResolver,
    IOperationSnapshotSecurity? snapshotSecurity = null
)
{
    private const int DefaultTimeoutSeconds = 300;
    private readonly IOperationSnapshotSecurity _snapshotSecurity =
        snapshotSecurity ?? new OperationSnapshotSecurity();

    public async Task<ManifestOperationResult<ExternalSourceMaterialization>> MaterializeAsync(
        ExternalSourceRequirementPlan plan,
        CancellationToken cancellationToken = default
    )
    {
        var workspace = fileSystem.Path.Combine(
            Path.GetTempPath(),
            "lunapack",
            "operations",
            Guid.NewGuid().ToString("N"),
            "external"
        );
        try
        {
            fileSystem.Directory.CreateDirectory(workspace);
            _snapshotSecurity.ApplyDirectory(workspace);
            var materialized = new Dictionary<string, MaterializedRoot>(StringComparer.Ordinal);
            var roots = new List<(string PackId, ExternalContentRoot Root)>();
            foreach (var group in plan.Groups)
            {
                var materializedGroup = await MaterializeGroupAsync(
                    group,
                    workspace,
                    materialized,
                    cancellationToken
                );
                if (materializedGroup.Value is not { } groupContent)
                {
                    return Failure(
                        workspace,
                        materializedGroup.Error
                            ?? $"Unable to materialize source '{group.WorkspaceSourceName}'."
                    );
                }

                AddRoots(roots, group, groupContent);
            }

            return ManifestOperationResult<ExternalSourceMaterialization>.Success(
                new ExternalSourceMaterialization(
                    fileSystem,
                    new ExternalContentRoots(roots),
                    workspace,
                    _snapshotSecurity
                )
            );
        }
        catch (Exception exception)
            when (exception
                    is IOException
                        or UnauthorizedAccessException
                        or ArgumentException
                        or NotSupportedException
            )
        {
            return Failure(
                workspace,
                $"Unable to materialize external sources: {exception.Message}"
            );
        }
    }

    private static void AddRoots(
        List<(string PackId, ExternalContentRoot Root)> roots,
        ExternalSourceRequirementGroup group,
        MaterializedGroup materializedGroup
    ) =>
        roots.AddRange(
            group.Uses.Select(use =>
                (
                    use.PackId,
                    new ExternalContentRoot(
                        use.Alias,
                        materializedGroup.Root.Directory,
                        group.WorkspaceSourceName,
                        group.Fingerprint.Value,
                        group.Fingerprint.Ref ?? string.Empty,
                        materializedGroup.ResolvedCommit
                    )
                )
            )
        );

    private async Task<ManifestOperationResult<MaterializedGroup>> MaterializeGroupAsync(
        ExternalSourceRequirementGroup group,
        string workspace,
        IDictionary<string, MaterializedRoot> materialized,
        CancellationToken cancellationToken
    )
    {
        var resolution = await gitRefResolver.ResolveAsync(
            group.Source,
            cachedDefaultBranch: null,
            cancellationToken
        );
        if (resolution.Value is not { } resolved)
        {
            return ManifestOperationResult<MaterializedGroup>.Failure(
                resolution.Error ?? $"Unable to resolve source '{group.WorkspaceSourceName}'."
            );
        }

        var cacheKey = $"{group.Fingerprint.Value}@{resolved.ResolvedCommit}";
        if (!materialized.TryGetValue(cacheKey, out var root))
        {
            var gitWorkspace = fileSystem.Path.Combine(
                workspace,
                materialized.Count.ToString(CultureInfo.InvariantCulture)
            );
            var result = await MaterializeRootAsync(
                group,
                resolved.ResolvedCommit,
                gitWorkspace,
                cancellationToken
            );
            if (result.Value is not { } createdRoot)
            {
                return ManifestOperationResult<MaterializedGroup>.Failure(
                    result.Error ?? $"Unable to materialize source '{group.WorkspaceSourceName}'."
                );
            }

            root = createdRoot;
            materialized.Add(cacheKey, root);
        }

        return ManifestOperationResult<MaterializedGroup>.Success(
            new MaterializedGroup(root, resolved.ResolvedCommit)
        );
    }

    private async Task<ManifestOperationResult<MaterializedRoot>> MaterializeRootAsync(
        ExternalSourceRequirementGroup group,
        string resolvedCommit,
        string workspace,
        CancellationToken cancellationToken
    )
    {
        var timeout = TimeSpan.FromSeconds(group.Source.TimeoutSeconds ?? DefaultTimeoutSeconds);
        fileSystem.Directory.CreateDirectory(workspace);
        foreach (
            var command in new[]
            {
                new[] { "init", "--quiet", workspace },
                ["-C", workspace, "remote", "add", "origin", group.Source.Url],
                [
                    "-C",
                    workspace,
                    "fetch",
                    "--depth=1",
                    "--filter=blob:none",
                    "origin",
                    resolvedCommit,
                ],
                ["-C", workspace, "sparse-checkout", "init", "--no-cone"],
                [
                    "-C",
                    workspace,
                    "sparse-checkout",
                    "set",
                    "--no-cone",
                    "--",
                    CreateSparsePattern(group.Source.Path),
                ],
                ["-C", workspace, "checkout", "--quiet", "--detach", "FETCH_HEAD"],
            }
        )
        {
            var result = await processRunner.RunAsync(command, timeout, cancellationToken);
            if (!result.IsSuccess)
            {
                return ManifestOperationResult<MaterializedRoot>.Failure(
                    result.Error ?? "External source Git materialization failed."
                );
            }
        }

        var root = GetContentRoot(workspace, group.Source.Path);
        if (!fileSystem.Directory.Exists(root))
        {
            return ManifestOperationResult<MaterializedRoot>.Failure(
                $"External source '{group.WorkspaceSourceName}' base path '{group.Fingerprint.Path}' is unavailable."
            );
        }

        var containment = ValidatePhysicalContainment(root);
        return containment.IsSuccess
            ? ManifestOperationResult<MaterializedRoot>.Success(new MaterializedRoot(root))
            : ManifestOperationResult<MaterializedRoot>.Failure(
                containment.Error ?? "External source contains an unsafe symbolic link."
            );
    }

    private ManifestOperationResult<bool> ValidatePhysicalContainment(string root)
    {
        var canonicalRoot = fileSystem.Path.GetFullPath(root);
        foreach (
            var path in fileSystem.Directory.EnumerateFileSystemEntries(
                root,
                "*",
                SearchOption.AllDirectories
            )
        )
        {
            if (!fileSystem.File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint))
            {
                continue;
            }

            var target = fileSystem.Directory.Exists(path)
                ? Directory.ResolveLinkTarget(path, returnFinalTarget: true)
                : File.ResolveLinkTarget(path, returnFinalTarget: true);
            if (target is null || !IsWithin(canonicalRoot, target.FullName))
            {
                return ManifestOperationResult<bool>.Failure(
                    $"External source link '{ProjectPath.Normalize(fileSystem.Path.GetRelativePath(root, path))}' resolves outside its source root."
                );
            }
        }

        return ManifestOperationResult<bool>.Success(true);
    }

    private bool IsWithin(string root, string path)
    {
        var candidate = fileSystem.Path.GetFullPath(path);
        var comparison =
            fileSystem.Path.DirectorySeparatorChar == '\\'
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;
        var rootPrefix =
            fileSystem.Path.TrimEndingDirectorySeparator(root)
            + fileSystem.Path.DirectorySeparatorChar;
        return string.Equals(candidate, root, comparison)
            || candidate.StartsWith(rootPrefix, comparison);
    }

    private static string CreateSparsePattern(string? path) =>
        string.IsNullOrEmpty(path) ? "/*" : $"/{path.Trim('/')}/**";

    private string GetContentRoot(string workspace, string? path) =>
        string.IsNullOrEmpty(path)
            ? workspace
            : fileSystem.Path.Combine([
                workspace,
                .. path.Split('/', StringSplitOptions.RemoveEmptyEntries),
            ]);

    private ManifestOperationResult<ExternalSourceMaterialization> Failure(
        string workspace,
        string error
    )
    {
        _snapshotSecurity.PrepareForDelete(fileSystem, workspace);
        GitTemporaryWorkspace.Delete(fileSystem, workspace);
        return ManifestOperationResult<ExternalSourceMaterialization>.Failure(error);
    }

    private sealed record MaterializedRoot(string Directory);

    private sealed record MaterializedGroup(MaterializedRoot Root, string ResolvedCommit);
}
