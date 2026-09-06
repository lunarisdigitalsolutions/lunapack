using Lunapack.Cli.Application.CommandExecution;

namespace Lunapack.Cli.Project;

internal sealed record PackInstanceSelection(
    ProjectConfiguration.RequestedPack RequestedRoot,
    ProjectLockFile.PackInstance Instance,
    ProjectLockFile.ResolvedPack ResolvedPack
)
{
    public static ManifestOperationResult<PackInstanceSelection> Select(
        ProjectState state,
        string packId,
        string? alias,
        string command
    )
    {
        var matches = state
            .Configuration.Packs.Where(pack =>
                string.Equals(pack.Id, packId, StringComparison.Ordinal)
            )
            .ToList();
        var requestedRoot =
            alias is not null
                ? matches.Find(pack =>
                    string.Equals(pack.GetInstanceIdentity().Alias, alias, StringComparison.Ordinal)
                )
            : matches.Count == 1 ? matches[0]
            : matches.Find(pack =>
                string.Equals(pack.GetInstanceIdentity().Alias, packId, StringComparison.Ordinal)
            );
        if (requestedRoot is null)
        {
            return ManifestOperationResult<PackInstanceSelection>.Failure(
                CreateSelectionError(packId, alias, command, matches)
            );
        }

        var identity = requestedRoot.GetInstanceIdentity();
        var instance = state.LockFile.Instances.Find(candidate =>
            string.Equals(candidate.Id, identity.PackId, StringComparison.Ordinal)
            && string.Equals(candidate.Name, identity.Alias, StringComparison.Ordinal)
        );
        var resolvedPack = instance is null
            ? null
            : state.LockFile.Packs.Find(pack => pack.Key == instance.RootResolution);
        return instance is null || resolvedPack is null
            ? ManifestOperationResult<PackInstanceSelection>.Failure(
                $"Lock file does not contain requested pack instance '{identity.PackId}/{identity.Alias}'."
            )
            : ManifestOperationResult<PackInstanceSelection>.Success(
                new PackInstanceSelection(requestedRoot, instance, resolvedPack)
            );
    }

    private static string CreateSelectionError(
        string packId,
        string? alias,
        string command,
        List<ProjectConfiguration.RequestedPack> matches
    )
    {
        if (alias is not null || matches.Count == 0)
        {
            return alias is null
                ? $"Pack '{packId}' is not installed."
                : $"Pack instance '{packId}/{alias}' is not installed.";
        }

        var aliases = matches
            .Select(pack => pack.GetInstanceIdentity().Alias)
            .OrderBy(value => value, StringComparer.Ordinal);
        return $"Pack '{packId}' has multiple instances. Specify --name. Available aliases: {string.Join(", ", aliases)}.{Environment.NewLine}"
            + string.Join(
                Environment.NewLine,
                aliases.Select(value => $"luna {command} {packId} --name {value}")
            );
    }
}
