namespace Lunapack.Cli;

internal sealed record TrustAcknowledgements
{
    public List<TrustedPackIdentity> Packs { get; set; } = [];

    public List<ConfiguredSourceIdentity> Sources { get; set; } = [];
}
