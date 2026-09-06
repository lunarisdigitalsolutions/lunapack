namespace Lunapack.Cli.Packs.ManagedFiles;

internal sealed record ManagedRootOwner(
    ManagedRootKind Kind,
    string Name,
    string? Version = null,
    string? InstanceName = null,
    bool IsLegacy = false
)
{
    public string Describe() =>
        Kind switch
        {
            ManagedRootKind.Link => $"link '{Name}'",
            ManagedRootKind.PackInstance => $"pack instance '{Name}/{InstanceName}'",
            _ => $"pack '{Name}'",
        };

    public bool Matches(ManagedRootOwner other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return Kind == other.Kind
            && string.Equals(Name, other.Name, StringComparison.Ordinal)
            && string.Equals(Version, other.Version, StringComparison.Ordinal)
            && string.Equals(InstanceName, other.InstanceName, StringComparison.Ordinal)
            && IsLegacy == other.IsLegacy;
    }
}
