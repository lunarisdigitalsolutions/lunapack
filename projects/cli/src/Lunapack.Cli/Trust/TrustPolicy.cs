using System.IO.Abstractions;
using Lunapack.Cli.Application.CommandExecution;
using Lunapack.Cli.Project;
using Lunapack.Cli.Sources;

namespace Lunapack.Cli.Trust;

internal sealed class TrustPolicy(IFileSystem fileSystem)
{
    public bool IsTrusted(
        string projectDirectory,
        string projectKey,
        ProjectConfiguration configuration,
        UserSettings settings,
        string sourceName,
        ConfiguredSourceIdentity resolvedSourceIdentity,
        string packId
    ) =>
        GetTrustScopes(
            projectDirectory,
            projectKey,
            configuration,
            settings,
            sourceName,
            resolvedSourceIdentity,
            packId
        ).Count > 0;

    public IReadOnlyList<TrustScope> GetTrustScopes(
        string projectDirectory,
        string projectKey,
        ProjectConfiguration configuration,
        UserSettings settings,
        string sourceName,
        ConfiguredSourceIdentity resolvedSourceIdentity,
        string packId
    )
    {
        var configuredSource = configuration.Sources.Find(source =>
            string.Equals(source.Name, sourceName, StringComparison.Ordinal)
        );
        if (configuredSource is null)
        {
            return [];
        }

        var currentIdentity = ConfiguredSourceIdentity.CreateForTrust(
            fileSystem,
            projectDirectory,
            configuredSource
        );
        var resolvedIdentity = CreateResolvedIdentityForTrust(
            projectDirectory,
            sourceName,
            resolvedSourceIdentity
        );
        if (
            currentIdentity.Value is not { } identity
            || resolvedIdentity.Value is not { } trustedIdentity
            || identity != trustedIdentity
        )
        {
            return [];
        }

        settings.Projects.TryGetValue(projectKey, out var localTrust);
        var scopes = new List<TrustScope>(3);
        if (
            localTrust is not null
            && IsAcknowledgedProjectTrust(
                configuration.Trust,
                localTrust.Acknowledgements,
                sourceName,
                identity,
                packId
            )
        )
        {
            scopes.Add(TrustScope.Project);
        }

        if (localTrust is not null && IsTrustedBy(localTrust, identity, packId))
        {
            scopes.Add(TrustScope.LocalUser);
        }

        if (IsTrustedBy(settings.Global, identity, packId))
        {
            scopes.Add(TrustScope.GlobalUser);
        }

        return scopes;
    }

    private ManifestOperationResult<ConfiguredSourceIdentity> CreateResolvedIdentityForTrust(
        string projectDirectory,
        string sourceName,
        ConfiguredSourceIdentity identity
    ) =>
        string.Equals(identity.Type, "local", StringComparison.Ordinal) && identity.Path is { } path
            ? ConfiguredSourceIdentity.CreateForTrust(
                fileSystem,
                projectDirectory,
                new ProjectConfiguration.LocalSource { Name = sourceName, Path = path }
            )
            : ManifestOperationResult<ConfiguredSourceIdentity>.Success(identity);

    private static bool IsTrustedBy(
        UserTrust trust,
        ConfiguredSourceIdentity source,
        string packId
    ) => trust.Sources.Contains(source) || ContainsPack(trust.Packs, source, packId);

    private static bool IsTrustedBy(
        LocalProjectTrust trust,
        ConfiguredSourceIdentity source,
        string packId
    ) => trust.Sources.Contains(source) || ContainsPack(trust.Packs, source, packId);

    private static bool IsAcknowledgedProjectTrust(
        ProjectConfiguration.ProjectTrust declarations,
        TrustAcknowledgements acknowledgements,
        string sourceName,
        ConfiguredSourceIdentity source,
        string packId
    )
    {
        var sourceTrusted =
            declarations.Sources.Contains(sourceName, StringComparer.Ordinal)
            && acknowledgements.Sources.Contains(source);
        var packTrusted =
            declarations.Packs.Exists(pack =>
                string.Equals(pack.Source, sourceName, StringComparison.Ordinal)
                && string.Equals(pack.Id, packId, StringComparison.Ordinal)
            ) && ContainsPack(acknowledgements.Packs, source, packId);
        return sourceTrusted || packTrusted;
    }

    private static bool ContainsPack(
        IReadOnlyList<TrustedPackIdentity> packs,
        ConfiguredSourceIdentity source,
        string packId
    ) =>
        packs.Any(pack =>
            pack.Source == source && string.Equals(pack.Id, packId, StringComparison.Ordinal)
        );
}
