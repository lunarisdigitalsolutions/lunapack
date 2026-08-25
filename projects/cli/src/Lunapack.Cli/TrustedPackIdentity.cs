namespace Lunapack.Cli;

internal sealed record TrustedPackIdentity
{
    public required string Id { get; set; }

    public required ConfiguredSourceIdentity Source { get; set; }
}
