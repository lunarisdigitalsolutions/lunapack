using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Lunapack.Cli.Links;

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
    public static bool Matches(string blobId, Stream contents, long contentLength)
    {
        ArgumentNullException.ThrowIfNull(blobId);
        ArgumentNullException.ThrowIfNull(contents);
        ArgumentOutOfRangeException.ThrowIfNegative(contentLength);

        if (blobId.Length is not 40 and not 64)
        {
            return false;
        }

        var header = Encoding.ASCII.GetBytes(
            $"blob {contentLength.ToString(CultureInfo.InvariantCulture)}\0"
        );
        using var hash = IncrementalHash.CreateHash(
            blobId.Length == 64 ? HashAlgorithmName.SHA256 : HashAlgorithmName.SHA1
        );
        hash.AppendData(header);
        var buffer = new byte[81920];
        long bytesRead = 0;
        while (true)
        {
            var read = contents.Read(buffer, 0, buffer.Length);
            if (read == 0)
            {
                break;
            }

            bytesRead = checked(bytesRead + read);
            if (bytesRead > contentLength)
            {
                return false;
            }

            hash.AppendData(buffer, 0, read);
        }

        return bytesRead == contentLength
            && string.Equals(
                Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant(),
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
