namespace Lunapack.Cli;

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
        return new GitSourceCacheIdentity(
            fingerprint.Value?.Value ?? $"{SourceFingerprint.GitType}:{identity.Url}",
            identity.Url!,
            identity.Ref,
            identity.Path
        );
    }
}
