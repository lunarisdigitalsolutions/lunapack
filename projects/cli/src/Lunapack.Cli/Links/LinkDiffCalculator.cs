namespace Lunapack.Cli;

internal static class LinkDiffCalculator
{
    public static LinkDiff Compare(
        ProjectLockFile.ResolvedLink? lockedLink,
        ResolvedLinkSnapshot snapshot
    )
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        if (lockedLink is null)
        {
            return new LinkDiff(
                DefinitionChanged: true,
                SourceIdentityChanged: false,
                CommitChanged: false,
                [
                    .. snapshot.Files.Select(file => new LinkFileChange(
                        null,
                        file,
                        LinkFileChangeKind.Added
                    )),
                ]
            );
        }

        return new LinkDiff(
            !string.Equals(
                lockedLink.DefinitionSha256,
                snapshot.DefinitionSha256,
                StringComparison.Ordinal
            ),
            lockedLink.SourceIdentity != snapshot.SourceIdentity,
            !string.Equals(
                lockedLink.GitSource?.ResolvedCommit,
                snapshot.GitSource?.ResolvedCommit,
                StringComparison.Ordinal
            ),
            ClassifyMoves(CompareFiles(lockedLink, snapshot))
        );
    }

    private static List<LinkFileChange> CompareFiles(
        ProjectLockFile.ResolvedLink lockedLink,
        ResolvedLinkSnapshot snapshot
    )
    {
        var lockedFiles = lockedLink.Files.ToDictionary(
            file => file.SourcePath,
            file => new ManagedRootFile(
                file.SourcePath,
                file.DeclaredTargetPath,
                file.TargetPath,
                file.Sha256
            ),
            StringComparer.Ordinal
        );
        var currentFiles = snapshot.Files.ToDictionary(
            file => file.SourcePath,
            StringComparer.Ordinal
        );

        var changes = new List<LinkFileChange>();
        foreach (var sourcePath in currentFiles.Keys.Order(StringComparer.Ordinal))
        {
            var currentFile = currentFiles[sourcePath];
            if (!lockedFiles.TryGetValue(sourcePath, out var lockedFile))
            {
                changes.Add(new LinkFileChange(null, currentFile, LinkFileChangeKind.Added));
                continue;
            }

            if (!IsUnchanged(lockedFile, currentFile))
            {
                changes.Add(
                    new LinkFileChange(lockedFile, currentFile, LinkFileChangeKind.Changed)
                );
            }
        }

        foreach (var sourcePath in lockedFiles.Keys.Order(StringComparer.Ordinal))
        {
            if (!currentFiles.ContainsKey(sourcePath))
            {
                changes.Add(
                    new LinkFileChange(lockedFiles[sourcePath], null, LinkFileChangeKind.Removed)
                );
            }
        }

        return changes;
    }

    private static bool IsUnchanged(ManagedRootFile lockedFile, ResolvedLinkFile currentFile) =>
        string.Equals(lockedFile.Sha256, currentFile.Sha256, StringComparison.Ordinal)
        && string.Equals(lockedFile.TargetPath, currentFile.TargetPath, StringComparison.Ordinal)
        && string.Equals(
            lockedFile.DeclaredTargetPath,
            currentFile.DeclaredTargetPath,
            StringComparison.Ordinal
        );

    private static IReadOnlyList<LinkFileChange> ClassifyMoves(List<LinkFileChange> changes)
    {
        var moves = new List<LinkFileChange>();
        var removed = changes.FindAll(change => change.Kind is LinkFileChangeKind.Removed);
        var added = changes.FindAll(change => change.Kind is LinkFileChangeKind.Added);
        foreach (var addedChange in added)
        {
            var digest = addedChange.CurrentFile!.Sha256;
            var addedMatches = added.Count(candidate =>
                string.Equals(candidate.CurrentFile!.Sha256, digest, StringComparison.Ordinal)
            );
            var removedMatches = removed.FindAll(candidate =>
                string.Equals(candidate.PreviousFile!.Sha256, digest, StringComparison.Ordinal)
            );
            if (addedMatches != 1 || removedMatches.Count != 1)
            {
                continue;
            }

            moves.Add(
                new LinkFileChange(
                    removedMatches[0].PreviousFile,
                    addedChange.CurrentFile,
                    LinkFileChangeKind.Moved
                )
            );
            changes.Remove(removedMatches[0]);
            changes.Remove(addedChange);
        }

        return [.. changes, .. moves];
    }
}
