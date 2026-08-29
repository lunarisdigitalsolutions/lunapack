using Lunapack.Cli.Project;
using Lunapack.Cli.Sources;

namespace Lunapack.Cli.Trust;

internal sealed record TrustListing
{
    public required TrustScope Scope { get; init; }

    public bool ScriptsDenied { get; init; }

    public IReadOnlyList<ConfiguredSourceIdentity> Sources { get; init; } = [];

    public IReadOnlyList<TrustedPackIdentity> Packs { get; init; } = [];

    public IReadOnlyList<string> ProjectSourceDeclarations { get; init; } = [];

    public IReadOnlyList<ProjectConfiguration.TrustedPack> ProjectPackDeclarations { get; init; } =
    [];

    public IReadOnlyList<ConfiguredSourceIdentity> ProjectSourceAcknowledgements { get; init; } =
    [];

    public IReadOnlyList<TrustedPackIdentity> ProjectPackAcknowledgements { get; init; } = [];
}
