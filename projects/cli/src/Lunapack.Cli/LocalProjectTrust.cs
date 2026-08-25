namespace Lunapack.Cli;

internal sealed record LocalProjectTrust
{
    public UserTrust Acknowledgements { get; set; } = new();

    public List<TrustedPackIdentity> Packs { get; set; } = [];

    public List<ConfiguredSourceIdentity> Sources { get; set; } = [];
}
