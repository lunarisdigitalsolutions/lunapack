using Lunapack.Cli.Sources;

namespace Lunapack.Cli.Trust;

internal sealed record TrustedPackIdentity
{
    public required string Id { get; set; }

    public required ConfiguredSourceIdentity Source { get; set; }
}
