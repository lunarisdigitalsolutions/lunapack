using System.IO.Abstractions;
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
    )
    {
        var configuredSource = configuration.Sources.Find(source =>
            string.Equals(source.Name, sourceName, StringComparison.Ordinal)
        );
        if (configuredSource is null)
        {
            return false;
        }

        var currentIdentity = ConfiguredSourceIdentity.CreateForTrust(
            fileSystem,
            projectDirectory,
            configuredSource
        );
        if (currentIdentity.Value is not { } identity || identity != resolvedSourceIdentity)
        {
            return false;
        }

        if (
            IsTrustedBy(settings.Global, identity, packId)
            || (
                settings.Projects.TryGetValue(projectKey, out var localTrust)
                && IsTrustedBy(localTrust, identity, packId)
            )
        )
        {
            return true;
        }

        return localTrust is not null
            && IsAcknowledgedProjectTrust(
                configuration.Trust,
                localTrust.Acknowledgements,
                sourceName,
                identity,
                packId
            );
    }

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
