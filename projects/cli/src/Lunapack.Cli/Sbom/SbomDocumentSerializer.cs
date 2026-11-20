using System.Buffers;
using System.Text;
using System.Text.Json;

namespace Lunapack.Cli.Sbom;

internal static class SbomDocumentSerializer
{
    public static string Serialize(
        SbomInventory inventory,
        SbomTarget target,
        DateTimeOffset created
    )
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = true }))
        {
            switch (target.Standard)
            {
                case SbomFormat.CycloneDx:
                    CycloneDxSbomWriter.Write(writer, inventory, target);
                    break;
                case SbomFormat.Spdx:
                    WriteSpdx(writer, inventory, target, created);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(target));
            }
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    private static void WriteSpdx(
        Utf8JsonWriter writer,
        SbomInventory inventory,
        SbomTarget target,
        DateTimeOffset created
    )
    {
        if (string.Equals(target.Name, "spdx-2.3", StringComparison.Ordinal))
        {
            Spdx23SbomWriter.Write(writer, inventory, target, created);
            return;
        }

        if (string.Equals(target.Name, "spdx-3.0.1", StringComparison.Ordinal))
        {
            Spdx301SbomWriter.Write(writer, inventory, target, created);
            return;
        }

        throw new ArgumentOutOfRangeException(nameof(target));
    }
}
