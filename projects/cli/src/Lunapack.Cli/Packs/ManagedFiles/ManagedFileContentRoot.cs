using Lunapack.Cli.Packs.ExternalSources;

namespace Lunapack.Cli.Packs.ManagedFiles;

internal sealed record ManagedFileContentRoot(string Directory, ExternalContentRoot? External);
