namespace Lunapack.Cli;

internal sealed record UserTrust
{
    public List<TrustedPackIdentity> Packs { get; set; } = [];

    public List<ConfiguredSourceIdentity> Sources { get; set; } = [];
}
