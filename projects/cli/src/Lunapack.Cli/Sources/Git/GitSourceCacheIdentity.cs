using Lunapack.Cli.Project;

namespace Lunapack.Cli.Sources.Git;

internal sealed record GitSourceCacheIdentity(
    string Fingerprint,
    string Url,
    string? Ref,
    string? Path
)
{
    public static GitSourceCacheIdentity Create(ProjectConfiguration.GitSource source)
    {
        var identity = ConfiguredSourceIdentity.Create(source);
        var fingerprint = SourceIdentityNormalizer.Create(source);
        var url = identity.Url ?? throw new InvalidOperationException("Git identity has no URL.");
        return new GitSourceCacheIdentity(
            fingerprint.Value?.Value ?? $"{SourceFingerprint.GitType}:{url}",
            url,
            identity.Ref,
            identity.Path
        );
    }
}
