using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Lunapack.Cli.Sbom;

internal static class SbomInventoryFingerprint
{
    public static string Create(SbomInventory inventory, SbomTarget target)
    {
        var inventoryBytes = JsonSerializer.SerializeToUtf8Bytes(
            inventory,
            SbomInventoryJsonContext.Default.SbomInventory
        );
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(Encoding.UTF8.GetBytes(target.Name));
        hash.AppendData([0]);
        hash.AppendData(inventoryBytes);
        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    public static string CreateDocument(
        SbomInventory inventory,
        SbomTarget target,
        DateTimeOffset created
    )
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(Encoding.UTF8.GetBytes(Create(inventory, target)));
        hash.AppendData([0]);
        hash.AppendData(
            Encoding.UTF8.GetBytes(
                created
                    .ToUniversalTime()
                    .UtcTicks.ToString(System.Globalization.CultureInfo.InvariantCulture)
            )
        );
        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }
}
