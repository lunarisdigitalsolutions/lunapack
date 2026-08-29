namespace Lunapack.Cli.Packs.ExternalSources;

internal sealed class ExternalContentRoots
{
    private const char KeySeparator = '\u001f';

    private readonly Dictionary<string, ExternalContentRoot> _roots;

    public ExternalContentRoots(IEnumerable<(string PackId, ExternalContentRoot Root)> roots)
    {
        _roots = new Dictionary<string, ExternalContentRoot>(StringComparer.Ordinal);
        foreach (var (packId, root) in roots)
        {
            _roots[CreateKey(packId, root.Alias)] = root;
        }
    }

    public static ExternalContentRoots Empty { get; } = new([]);

    public ExternalContentRoot? Find(string packId, string alias) =>
        _roots.GetValueOrDefault(CreateKey(packId, alias));

    public IReadOnlyDictionary<string, ExternalContentRoot> ForPack(string packId)
    {
        var prefix = $"{packId}{KeySeparator}";
        return _roots
            .Where(entry => entry.Key.StartsWith(prefix, StringComparison.Ordinal))
            .ToDictionary(entry => entry.Value.Alias, entry => entry.Value, StringComparer.Ordinal);
    }

    private static string CreateKey(string packId, string alias) =>
        $"{packId}{KeySeparator}{alias}";
}
