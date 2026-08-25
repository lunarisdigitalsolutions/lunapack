namespace Lunapack.Cli;

internal sealed record GitSourceCacheIdentity(string Url, string? Ref, string? Path)
{
    public static GitSourceCacheIdentity Create(ProjectConfiguration.GitSource source)
    {
        var identity = ConfiguredSourceIdentity.Create(source);
        return new GitSourceCacheIdentity(identity.Url!, identity.Ref, identity.Path);
    }
}
