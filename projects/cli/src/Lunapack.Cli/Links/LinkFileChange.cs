namespace Lunapack.Cli;

internal sealed record LinkFileChange(
    ManagedRootFile? PreviousFile,
    ResolvedLinkFile? CurrentFile,
    LinkFileChangeKind Kind
);
