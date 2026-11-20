using System.Security.Cryptography;
using System.Text;

namespace Lunapack.Cli.Sbom;

internal static class SbomIdentifiers
{
    public static string PackReference(string id, string version) =>
        $"pkg:generic/lunapack/{Uri.EscapeDataString(id)}@{Uri.EscapeDataString(version)}";

    public static string LinkReference(string name) =>
        $"urn:lunapack:link:{Uri.EscapeDataString(name)}";

    public static string ManagedFileReference(string ownerReference, string targetPath)
    {
        var identity = Encoding.UTF8.GetBytes($"{ownerReference}\0{targetPath}");
        var digest = Convert.ToHexString(SHA256.HashData(identity)).ToLowerInvariant();
        return $"urn:lunapack:file:sha256:{digest}";
    }

    public static string Spdx2ElementId(string kind, string reference) =>
        $"SPDXRef-{kind}-{Hash($"{kind}\0{reference}")}";

    public static string Spdx3ElementId(string kind, string reference) =>
        $"urn:lunapack:spdx:3.0.1:{kind}:{Hash($"{kind}\0{reference}")}";

    public static string Spdx3DocumentId(string inventoryFingerprint) =>
        $"https://lunapack.dev/sbom/spdx-3.0.1/{inventoryFingerprint}#document";

    public static string Spdx3CreationInfoId(string inventoryFingerprint) =>
        $"https://lunapack.dev/sbom/spdx-3.0.1/{inventoryFingerprint}#creation-info";

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}
