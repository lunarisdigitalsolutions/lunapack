namespace Lunapack.Cli;

internal sealed record ManagedRootOwner(ManagedRootKind Kind, string Name, string? Version = null)
{
    public string Describe() => Kind is ManagedRootKind.Link ? $"link '{Name}'" : $"pack '{Name}'";

    public bool Matches(ManagedRootOwner other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return Kind == other.Kind && string.Equals(Name, other.Name, StringComparison.Ordinal);
    }
}
