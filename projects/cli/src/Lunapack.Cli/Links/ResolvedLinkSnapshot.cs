namespace Lunapack.Cli;

internal sealed record ResolvedLinkSnapshot(
    string Name,
    string DefinitionSha256,
    string SourceName,
    ConfiguredSourceIdentity SourceIdentity,
    GitSourceProvenance? GitSource,
    IReadOnlyList<ResolvedLinkFile> Files
)
{
    public ManagedRoot ToManagedRoot() =>
        new(
            new ManagedRootOwner(ManagedRootKind.Link, Name),
            SourceName,
            SourceIdentity,
            GitSource,
            [
                .. Files.Select(file => new ManagedRootFile(
                    file.SourcePath,
                    file.DeclaredTargetPath,
                    file.TargetPath,
                    file.Sha256
                )),
            ]
        );

    public ProjectLockFile.ResolvedLink ToLockRecord() =>
        new()
        {
            DefinitionSha256 = DefinitionSha256,
            GitSource = GitSource,
            SourceIdentity = SourceIdentity,
            SourceName = SourceName,
            Files =
            [
                .. Files.Select(file => new ProjectLockFile.LinkFile
                {
                    DeclaredTargetPath = file.DeclaredTargetPath,
                    Sha256 = file.Sha256,
                    SourcePath = file.SourcePath,
                    TargetPath = file.TargetPath,
                }),
            ],
        };
}
