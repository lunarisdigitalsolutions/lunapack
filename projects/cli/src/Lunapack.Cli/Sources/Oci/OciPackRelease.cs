using Lunapack.Cli.Packs.Manifest;
using NuGet.Versioning;

namespace Lunapack.Cli.Sources.Oci;

internal sealed record OciPackRelease(
    PackManifest Manifest,
    NuGetVersion Version,
    string PackDirectory,
    OciSourceProvenance Provenance
);
