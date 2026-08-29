using Lunapack.Cli.Packs.ManagedFiles;

namespace Lunapack.Cli.Links;

internal sealed record LinkFileChange(
    ManagedRootFile? PreviousFile,
    ResolvedLinkFile? CurrentFile,
    LinkFileChangeKind Kind
);
