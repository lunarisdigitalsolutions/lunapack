using Lunapack.Cli.Application.Diagnostics;
using Lunapack.Cli.Links;

namespace Lunapack.Cli.Packs.Planning;

internal static class OutdatedJsonMapper
{
    public static CliJsonOutdatedResult Map(
        PackUpdateAvailability availability,
        IReadOnlyList<LinkOutdatedReport> outdatedLinks,
        bool remoteRefsChecked
    ) =>
        new(
            [
                .. availability
                    .Updates.OrderBy(update => update.RequestedRoot.Id, StringComparer.Ordinal)
                    .ThenBy(update => update.Current.Version, StringComparer.Ordinal)
                    .Select(update => new CliJsonOutdatedResult.Update(
                        update.RequestedRoot.Id,
                        update.Current.Version,
                        update.Latest.Manifest.Version,
                        update.Reason
                    )),
            ],
            [
                .. outdatedLinks
                    .OrderBy(link => link.Name, StringComparer.Ordinal)
                    .Select(link => new CliJsonOutdatedResult.OutdatedLink(
                        link.Name,
                        [.. link.Reasons.OrderBy(reason => reason, StringComparer.Ordinal)]
                    )),
            ],
            [
                .. availability
                    .IntegrityViolations.OrderBy(
                        violation => violation.LockedPack.Id,
                        StringComparer.Ordinal
                    )
                    .ThenBy(violation => violation.LockedPack.Version, StringComparer.Ordinal)
                    .Select(MapIntegrityViolation),
            ],
            [
                .. availability
                    .UncheckedOciPacks.OrderBy(pack => pack.Id, StringComparer.Ordinal)
                    .ThenBy(pack => pack.Version, StringComparer.Ordinal)
                    .Select(pack => new CliJsonOutdatedResult.UncheckedOciPack(
                        pack.Id,
                        pack.Version,
                        pack.SourceName
                    )),
            ],
            remoteRefsChecked
        );

    private static CliJsonOutdatedResult.IntegrityViolation MapIntegrityViolation(
        OciReleaseIntegrityViolation violation
    ) =>
        new(
            violation.LockedPack.Id,
            violation.LockedPack.Version,
            violation.LockedPack.SourceName ?? violation.ObservedPack?.SourceName,
            violation.LockedPack.OciSource?.ManifestDigest
                ?? throw new InvalidOperationException(
                    $"Locked OCI pack '{violation.LockedPack.Id}@{violation.LockedPack.Version}' has no manifest digest."
                ),
            violation.ObservedPack?.OciSource?.ManifestDigest
        );
}
