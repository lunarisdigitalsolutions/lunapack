using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Lunapack.Cli;

internal static class GitObjectId
{
    public static bool Matches(string blobId, byte[] contents)
    {
        ArgumentNullException.ThrowIfNull(blobId);

        return string.Equals(
            ComputeBlobId(contents, blobId.Length),
            blobId,
            StringComparison.Ordinal
        );
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Security",
        "CA5350:Do Not Use Weak Cryptographic Algorithms",
        Justification = "Git object identifiers are defined by Git as SHA-1 digests and are used only to verify cached content against Git."
    )]
    public static string ComputeBlobId(byte[] contents, int hexLength)
    {
        ArgumentNullException.ThrowIfNull(contents);

        var header = Encoding.ASCII.GetBytes(
            $"blob {contents.Length.ToString(CultureInfo.InvariantCulture)}\0"
        );
        var payload = new byte[header.Length + contents.Length];
        header.CopyTo(payload, 0);
        contents.CopyTo(payload, header.Length);

        var digest = hexLength == 64 ? SHA256.HashData(payload) : SHA1.HashData(payload);
        return Convert.ToHexString(digest).ToLowerInvariant();
    }
}
