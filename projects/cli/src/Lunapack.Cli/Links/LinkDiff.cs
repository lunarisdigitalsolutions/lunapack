namespace Lunapack.Cli.Links;

internal sealed record LinkDiff(
    bool DefinitionChanged,
    bool SourceIdentityChanged,
    bool CommitChanged,
    IReadOnlyList<LinkFileChange> Changes
)
{
    public bool HasFileChanges => Changes.Count > 0;

    public bool IsCurrent => !DefinitionChanged && !HasFileChanges;

    public IReadOnlyList<string> DescribeReasons()
    {
        var reasons = new List<string>();
        if (DefinitionChanged)
        {
            reasons.Add("definition changed");
        }

        if (SourceIdentityChanged)
        {
            reasons.Add("source identity changed");
        }

        foreach (var kind in Changes.Select(change => change.Kind).Distinct().Order())
        {
            reasons.Add(
                kind switch
                {
                    LinkFileChangeKind.Added => "files added",
                    LinkFileChangeKind.Removed => "files removed",
                    LinkFileChangeKind.Moved => "files moved",
                    _ => "file contents changed",
                }
            );
        }

        return reasons;
    }
}
