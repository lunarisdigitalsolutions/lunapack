namespace Lunapack.Cli;

internal sealed record LocalProjectTrust
{
    public TrustAcknowledgements Acknowledgements { get; set; } = new();

    public ScriptDenial? Deny { get; set; }

    public List<TrustedPackIdentity> Packs { get; set; } = [];

    public List<ConfiguredSourceIdentity> Sources { get; set; } = [];
}
