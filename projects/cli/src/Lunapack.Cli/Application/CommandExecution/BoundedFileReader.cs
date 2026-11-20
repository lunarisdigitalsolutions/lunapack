using System.IO.Abstractions;
using System.Text;

namespace Lunapack.Cli.Application.CommandExecution;

internal static class BoundedFileReader
{
    public static ManifestOperationResult<byte[]> ReadBytes(
        IFileSystem fileSystem,
        string path,
        long byteLimit,
        string inputName
    )
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentException.ThrowIfNullOrWhiteSpace(inputName);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(byteLimit);

        using var input = fileSystem.File.OpenRead(path);
        using var buffer = new MemoryStream();
        var readBuffer = new byte[81_920];
        long bytesRead = 0;
        while (true)
        {
            var read = input.Read(readBuffer, 0, readBuffer.Length);
            if (read == 0)
            {
                break;
            }

            var observedBytes = checked(bytesRead + read);
            if (observedBytes > byteLimit)
            {
                return ManifestOperationResult<byte[]>.Failure(
                    $"{inputName} exceeds its {byteLimit} byte limit (at least {observedBytes} bytes read).",
                    ManifestOperationErrorKind.ResourceLimitExceeded
                );
            }

            buffer.Write(readBuffer, 0, read);
            bytesRead = observedBytes;
        }

        return ManifestOperationResult<byte[]>.Success(buffer.ToArray());
    }

    public static async Task<ManifestOperationResult<Contents>> ReadTextAsync(
        IFileSystem fileSystem,
        string path,
        long byteLimit,
        string inputName
    )
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentException.ThrowIfNullOrWhiteSpace(inputName);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(byteLimit);

        await using var input = fileSystem.File.OpenRead(path);
        await using var buffer = new MemoryStream();
        var readBuffer = new byte[81_920];
        long bytesRead = 0;
        while (true)
        {
            var read = await input.ReadAsync(readBuffer.AsMemory());
            if (read == 0)
            {
                break;
            }

            var observedBytes = checked(bytesRead + read);
            if (observedBytes > byteLimit)
            {
                return ManifestOperationResult<Contents>.Failure(
                    $"{inputName} exceeds its {byteLimit} byte limit (at least {observedBytes} bytes read).",
                    ManifestOperationErrorKind.ResourceLimitExceeded
                );
            }

            await buffer.WriteAsync(readBuffer.AsMemory(0, read));
            bytesRead = observedBytes;
        }

        buffer.Position = 0;
        using var reader = new StreamReader(
            buffer,
            Encoding.UTF8,
            detectEncodingFromByteOrderMarks: true,
            bufferSize: 1024,
            leaveOpen: true
        );
        var text = await reader.ReadToEndAsync();
        return ManifestOperationResult<Contents>.Success(new Contents(text, bytesRead));
    }

    internal sealed record Contents(string Text, long ByteCount);
}
