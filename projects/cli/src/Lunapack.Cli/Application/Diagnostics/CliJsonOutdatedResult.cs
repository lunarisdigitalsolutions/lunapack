using System.Text.Json.Serialization;

namespace Lunapack.Cli.Application.Diagnostics;

internal sealed record CliJsonOutdatedResult(
    [property: JsonPropertyName("updates")] IReadOnlyList<CliJsonOutdatedResult.Update> Updates,
    [property: JsonPropertyName("outdatedLinks")]
        IReadOnlyList<CliJsonOutdatedResult.OutdatedLink> OutdatedLinks,
    [property: JsonPropertyName("integrityViolations")]
        IReadOnlyList<CliJsonOutdatedResult.IntegrityViolation> IntegrityViolations,
    [property: JsonPropertyName("uncheckedOciPacks")]
        IReadOnlyList<CliJsonOutdatedResult.UncheckedOciPack> UncheckedOciPacks,
    [property: JsonPropertyName("remoteRefsChecked")] bool RemoteRefsChecked
)
{
    internal sealed record Update(
        [property: JsonPropertyName("id")] string Id,
        [property: JsonPropertyName("currentVersion")] string CurrentVersion,
        [property: JsonPropertyName("latestVersion")] string LatestVersion,
        [property: JsonPropertyName("reason")] string Reason
    );

    internal sealed record OutdatedLink(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("reasons")] IReadOnlyList<string> Reasons
    );

    internal sealed record IntegrityViolation(
        [property: JsonPropertyName("id")] string Id,
        [property: JsonPropertyName("version")] string Version,
        [property: JsonPropertyName("sourceName")]
        [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)]
            string? SourceName,
        [property: JsonPropertyName("lockedManifestDigest")] string LockedManifestDigest,
        [property: JsonPropertyName("observedManifestDigest")]
        [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)]
            string? ObservedManifestDigest
    );

    internal sealed record UncheckedOciPack(
        [property: JsonPropertyName("id")] string Id,
        [property: JsonPropertyName("version")] string Version,
        [property: JsonPropertyName("sourceName")]
        [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)]
            string? SourceName
    );
}
