using System.IO.Abstractions;
using System.IO.Compression;
using System.Security.Cryptography;
using Lunapack.Cli.Application.CommandExecution;
using Lunapack.Cli.Application.Paths;

namespace Lunapack.Cli.Sources.Oci;

internal static class OciPackArchive
{
    public const int MaximumEntryCount = 100_000;
    public const long MaximumArchiveBytes = 512L * 1024 * 1024;
    public const long MaximumEntryBytes = 512L * 1024 * 1024;
    public const long MaximumExpandedBytes = 2L * 1024 * 1024 * 1024;

    private static readonly DateTimeOffset _reproducibleTimestamp = new(
        1980,
        1,
        1,
        0,
        0,
        0,
        TimeSpan.Zero
    );

    public static async Task<ManifestOperationResult<OciBlobTransfer>> CreateAsync(
        IFileSystem fileSystem,
        string packDirectory,
        Stream destination,
        CancellationToken cancellationToken
    )
    {
        var sourceEntries = EnumerateSourceEntries(fileSystem, packDirectory);
        if (sourceEntries.Value is not { } entries)
        {
            return ManifestOperationResult<OciBlobTransfer>.Failure(
                sourceEntries.Error ?? "Unable to enumerate pack content."
            );
        }

        if (entries.Count > MaximumEntryCount)
        {
            return ManifestOperationResult<OciBlobTransfer>.Failure(
                "Pack contains more than 100,000 archive entries."
            );
        }

        using var output = new BoundedHashingWriteStream(destination, MaximumArchiveBytes);
        try
        {
            long expandedBytes = 0;
            using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
            {
                foreach (var sourceEntry in entries)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var archiveName = sourceEntry.IsDirectory
                        ? $"{sourceEntry.RelativePath}/"
                        : sourceEntry.RelativePath;
                    var entry = archive.CreateEntry(archiveName, CompressionLevel.Optimal);
                    entry.LastWriteTime = _reproducibleTimestamp;
                    if (sourceEntry.IsDirectory)
                    {
                        continue;
                    }

                    await using var source = fileSystem.File.OpenRead(sourceEntry.FullPath);
                    await using var target = entry.Open();
                    await CopyBoundedAsync(
                        source,
                        target,
                        sourceEntry.Length,
                        MaximumEntryBytes,
                        MaximumExpandedBytes - expandedBytes,
                        cancellationToken
                    );
                    expandedBytes += sourceEntry.Length;
                }
            }

            return ManifestOperationResult<OciBlobTransfer>.Success(
                new OciBlobTransfer(output.BytesWritten, output.GetDigest())
            );
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
            when (exception
                    is IOException
                        or UnauthorizedAccessException
                        or InvalidDataException
                        or ArgumentException
            )
        {
            return ManifestOperationResult<OciBlobTransfer>.Failure(
                $"Unable to create a safe OCI pack archive: {exception.Message}"
            );
        }
    }

    public static async Task<ManifestOperationResult<IReadOnlyList<string>>> ExtractAsync(
        IFileSystem fileSystem,
        Stream archiveStream,
        string extractionRoot,
        CancellationToken cancellationToken
    )
    {
        try
        {
            using var archive = new ZipArchive(archiveStream, ZipArchiveMode.Read, leaveOpen: true);
            var validated = ValidateEntries(archive);
            if (validated.Value is not { } preparedEntries)
            {
                return ManifestOperationResult<IReadOnlyList<string>>.Failure(
                    validated.Error ?? "OCI pack archive contains unsafe entries."
                );
            }

            if (fileSystem.Directory.Exists(extractionRoot))
            {
                return ManifestOperationResult<IReadOnlyList<string>>.Failure(
                    "OCI pack extraction requires a new private temporary directory."
                );
            }

            fileSystem.Directory.CreateDirectory(extractionRoot);
            return ManifestOperationResult<IReadOnlyList<string>>.Success(
                await ExtractEntriesAsync(
                    fileSystem,
                    archive,
                    preparedEntries,
                    extractionRoot,
                    cancellationToken
                )
            );
        }
        catch (OperationCanceledException)
        {
            var cleanupError = DeleteExtractionRoot(fileSystem, extractionRoot);
            if (cleanupError is not null)
            {
                throw new IOException(
                    $"OCI pack extraction was cancelled and temporary cleanup failed: {cleanupError}"
                );
            }

            throw;
        }
        catch (Exception exception)
            when (exception
                    is IOException
                        or UnauthorizedAccessException
                        or InvalidDataException
                        or ArgumentException
                        or NotSupportedException
                        or OverflowException
            )
        {
            var cleanupError = DeleteExtractionRoot(fileSystem, extractionRoot);
            return ManifestOperationResult<IReadOnlyList<string>>.Failure(
                cleanupError is null
                    ? $"Unable to safely extract OCI pack content: {exception.Message}"
                    : $"Unable to safely extract OCI pack content: {exception.Message} Temporary cleanup also failed: {cleanupError}"
            );
        }
    }

    private static async Task<IReadOnlyList<string>> ExtractEntriesAsync(
        IFileSystem fileSystem,
        ZipArchive archive,
        IReadOnlyList<ValidatedEntry> entries,
        string extractionRoot,
        CancellationToken cancellationToken
    )
    {
        var root = fileSystem.Path.GetFullPath(extractionRoot);
        long expandedBytes = 0;
        foreach (var entry in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var targetPath = fileSystem.Path.GetFullPath(
                fileSystem.Path.Combine(root, entry.RelativePath)
            );
            if (!IsContainedPath(fileSystem, root, targetPath))
            {
                throw new InvalidDataException(
                    "OCI pack archive contains a path outside its extraction root."
                );
            }

            if (entry.IsDirectory)
            {
                fileSystem.Directory.CreateDirectory(targetPath);
                continue;
            }

            var parentDirectory = fileSystem.Path.GetDirectoryName(targetPath);
            if (parentDirectory is not null)
            {
                fileSystem.Directory.CreateDirectory(parentDirectory);
            }

            await using var source = entry.Entry.Open();
            await using var target = fileSystem.File.Open(
                targetPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None
            );
            expandedBytes += await CopyBoundedAsync(
                source,
                target,
                entry.DeclaredLength,
                MaximumEntryBytes,
                MaximumExpandedBytes - expandedBytes,
                cancellationToken
            );
        }

        return entries
            .Where(entry => !entry.IsDirectory)
            .Select(entry => entry.RelativePath)
            .ToArray();
    }

    private static ManifestOperationResult<List<SourceEntry>> EnumerateSourceEntries(
        IFileSystem fileSystem,
        string packDirectory
    )
    {
        try
        {
            var root = fileSystem.Path.GetFullPath(packDirectory);
            var rootInfo = fileSystem.DirectoryInfo.New(root);
            if (
                !rootInfo.Exists
                || rootInfo.Attributes.HasFlag(FileAttributes.ReparsePoint)
                || !rootInfo.Attributes.HasFlag(FileAttributes.Directory)
            )
            {
                return ManifestOperationResult<List<SourceEntry>>.Failure(
                    "Pack directory must be an existing regular directory, not a link."
                );
            }

            var entries = new List<SourceEntry>();
            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            long expandedBytes = 0;

            void Visit(string directory)
            {
                foreach (
                    var item in fileSystem
                        .DirectoryInfo.New(directory)
                        .EnumerateFileSystemInfos()
                        .OrderBy(item => item.Name, StringComparer.Ordinal)
                )
                {
                    if (item.Attributes.HasFlag(FileAttributes.ReparsePoint))
                    {
                        throw new InvalidDataException(
                            $"Pack content '{item.Name}' contains a symbolic link or reparse point."
                        );
                    }

                    var relativePath = ProjectPath.Normalize(
                        fileSystem.Path.GetRelativePath(root, item.FullName)
                    );
                    var canonicalPath = NormalizeEntryPath(relativePath, isDirectory: false);
                    if (!paths.Add(canonicalPath))
                    {
                        throw new InvalidDataException(
                            $"Pack content contains a duplicate path '{canonicalPath}'."
                        );
                    }

                    var isDirectory = item.Attributes.HasFlag(FileAttributes.Directory);
                    entries.Add(
                        new SourceEntry(
                            item.FullName,
                            canonicalPath,
                            isDirectory,
                            isDirectory ? 0 : fileSystem.FileInfo.New(item.FullName).Length
                        )
                    );
                    if (entries.Count > MaximumEntryCount)
                    {
                        throw new InvalidDataException(
                            "Pack contains more than 100,000 archive entries."
                        );
                    }

                    if (isDirectory)
                    {
                        Visit(item.FullName);
                        continue;
                    }

                    var length = entries[^1].Length;
                    if (length < 0 || length > MaximumEntryBytes)
                    {
                        throw new InvalidDataException(
                            $"Pack file '{canonicalPath}' exceeds the 512 MiB per-file limit."
                        );
                    }

                    expandedBytes = checked(expandedBytes + length);
                    if (expandedBytes > MaximumExpandedBytes)
                    {
                        throw new InvalidDataException(
                            "Pack content exceeds the 2 GiB expanded-size limit."
                        );
                    }
                }
            }

            Visit(root);
            return ManifestOperationResult<List<SourceEntry>>.Success(
                entries.OrderBy(entry => entry.RelativePath, StringComparer.Ordinal).ToList()
            );
        }
        catch (Exception exception)
            when (exception
                    is IOException
                        or UnauthorizedAccessException
                        or InvalidDataException
                        or ArgumentException
                        or OverflowException
            )
        {
            return ManifestOperationResult<List<SourceEntry>>.Failure(
                $"Unable to enumerate safe pack content: {exception.Message}"
            );
        }
    }

    private static ManifestOperationResult<IReadOnlyList<ValidatedEntry>> ValidateEntries(
        ZipArchive archive
    )
    {
        if (archive.Entries.Count > MaximumEntryCount)
        {
            return ManifestOperationResult<IReadOnlyList<ValidatedEntry>>.Failure(
                "OCI pack archive contains more than 100,000 entries."
            );
        }

        var entries = new List<ValidatedEntry>(archive.Entries.Count);
        var state = new EntryValidationState();
        foreach (var entry in archive.Entries)
        {
            var validation = ValidateEntry(entry, state);
            if (validation.Value is not { } validatedEntry)
            {
                return ManifestOperationResult<IReadOnlyList<ValidatedEntry>>.Failure(
                    validation.Error ?? "OCI pack archive contains an unsafe entry."
                );
            }

            entries.Add(validatedEntry);
        }

        return ManifestOperationResult<IReadOnlyList<ValidatedEntry>>.Success(entries);
    }

    private static ManifestOperationResult<ValidatedEntry> ValidateEntry(
        ZipArchiveEntry entry,
        EntryValidationState state
    )
    {
        var isDirectory = IsDirectoryEntry(entry);
        var relativePath = NormalizeEntryPath(ProjectPath.Normalize(entry.FullName), isDirectory);
        if (IsLinkEntry(entry))
        {
            return ManifestOperationResult<ValidatedEntry>.Failure(
                $"OCI pack archive entry '{relativePath}' is link-like and is not allowed."
            );
        }

        if (!state.ExplicitPaths.Add(relativePath))
        {
            return ManifestOperationResult<ValidatedEntry>.Failure(
                $"OCI pack archive contains a duplicate path '{relativePath}'."
            );
        }

        var entryLength = entry.Length;
        if (entryLength < 0 || entryLength > MaximumEntryBytes)
        {
            return ManifestOperationResult<ValidatedEntry>.Failure(
                $"OCI pack archive entry '{relativePath}' exceeds the 512 MiB per-file limit."
            );
        }

        var validation = isDirectory
            ? ValidateDirectory(relativePath, entryLength, state)
            : ValidateFile(relativePath, entryLength, state);
        return validation.IsSuccess
            ? ManifestOperationResult<ValidatedEntry>.Success(
                new ValidatedEntry(entry, relativePath, isDirectory, entryLength)
            )
            : ManifestOperationResult<ValidatedEntry>.Failure(
                validation.Error ?? "OCI pack archive contains conflicting entries."
            );
    }

    private static ManifestOperationResult<bool> ValidateDirectory(
        string relativePath,
        long entryLength,
        EntryValidationState state
    )
    {
        if (entryLength != 0 || state.Files.Contains(relativePath))
        {
            return ManifestOperationResult<bool>.Failure(
                $"OCI pack archive has conflicting file and directory paths at '{relativePath}'."
            );
        }

        state.Directories.Add(relativePath);
        return RegisterParentDirectories(relativePath, state);
    }

    private static ManifestOperationResult<bool> ValidateFile(
        string relativePath,
        long entryLength,
        EntryValidationState state
    )
    {
        if (state.Directories.Contains(relativePath))
        {
            return ManifestOperationResult<bool>.Failure(
                $"OCI pack archive has conflicting file and directory paths at '{relativePath}'."
            );
        }

        state.Files.Add(relativePath);
        state.DeclaredExpandedBytes = checked(state.DeclaredExpandedBytes + entryLength);
        if (state.DeclaredExpandedBytes > MaximumExpandedBytes)
        {
            return ManifestOperationResult<bool>.Failure(
                "OCI pack archive exceeds the 2 GiB expanded-size limit."
            );
        }

        return RegisterParentDirectories(relativePath, state);
    }

    private static ManifestOperationResult<bool> RegisterParentDirectories(
        string relativePath,
        EntryValidationState state
    )
    {
        var parent = relativePath;
        var separatorIndex = parent.LastIndexOf('/');
        while (separatorIndex >= 0)
        {
            parent = ProjectPath.Normalize(parent[..separatorIndex]);
            if (state.Files.Contains(parent))
            {
                return ManifestOperationResult<bool>.Failure(
                    $"OCI pack archive has a file-directory conflict at '{parent}'."
                );
            }

            state.Directories.Add(parent);
            separatorIndex = parent.LastIndexOf('/');
        }

        return ManifestOperationResult<bool>.Success(true);
    }

    private static bool IsDirectoryEntry(ZipArchiveEntry entry)
    {
        var attributes = (FileAttributes)entry.ExternalAttributes;
        var unixFileType = (entry.ExternalAttributes >> 16) & 0xF000;
        return entry.FullName.EndsWith('/')
            || entry.FullName.EndsWith('\\')
            || attributes.HasFlag(FileAttributes.Directory)
            || unixFileType == 0x4000;
    }

    private static bool IsLinkEntry(ZipArchiveEntry entry)
    {
        var attributes = (FileAttributes)entry.ExternalAttributes;
        var unixFileType = (entry.ExternalAttributes >> 16) & 0xF000;
        return attributes.HasFlag(FileAttributes.ReparsePoint) || unixFileType == 0xA000;
    }

    private static string NormalizeEntryPath(string value, bool isDirectory)
    {
        var path = ProjectPath.Normalize(value);
        if (
            path.Length == 0
            || path.StartsWith('/')
            || path.Length >= 2 && char.IsAsciiLetter(path[0]) && path[1] == ':'
        )
        {
            throw new InvalidDataException("OCI pack archive contains a rooted path.");
        }

        if (isDirectory)
        {
            path = path.TrimEnd('/');
        }

        var segments = path.Split('/');
        if (
            segments.Length == 0
            || segments.Any(segment =>
                segment.Length == 0 || segment is "." or ".." || segment.Contains(':')
            )
        )
        {
            throw new InvalidDataException("OCI pack archive contains an unsafe path.");
        }

        return string.Join('/', segments);
    }

    private static bool IsContainedPath(IFileSystem fileSystem, string root, string path)
    {
        var prefix =
            root.TrimEnd(
                fileSystem.Path.DirectorySeparatorChar,
                fileSystem.Path.AltDirectorySeparatorChar
            ) + fileSystem.Path.DirectorySeparatorChar;
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        return path.StartsWith(prefix, comparison);
    }

    private static async Task<long> CopyBoundedAsync(
        Stream source,
        Stream destination,
        long expectedLength,
        long maximumEntryBytes,
        long remainingTotalBytes,
        CancellationToken cancellationToken
    )
    {
        var buffer = new byte[81920];
        long copied = 0;
        while (true)
        {
            var read = await source.ReadAsync(buffer, cancellationToken);
            if (read == 0)
            {
                break;
            }

            copied = checked(copied + read);
            if (copied > maximumEntryBytes || copied > remainingTotalBytes)
            {
                throw new InvalidDataException("OCI pack archive exceeds an extraction limit.");
            }

            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }

        if (copied != expectedLength)
        {
            throw new InvalidDataException(
                "OCI pack archive entry size does not match its declared length."
            );
        }

        return copied;
    }

    private static string? DeleteExtractionRoot(IFileSystem fileSystem, string extractionRoot)
    {
        try
        {
            if (fileSystem.Directory.Exists(extractionRoot))
            {
                fileSystem.Directory.Delete(extractionRoot, recursive: true);
            }

            return null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return exception.Message;
        }
    }

    private sealed record SourceEntry(
        string FullPath,
        string RelativePath,
        bool IsDirectory,
        long Length
    );

    private sealed record ValidatedEntry(
        ZipArchiveEntry Entry,
        string RelativePath,
        bool IsDirectory,
        long DeclaredLength
    );

    private sealed class EntryValidationState
    {
        public long DeclaredExpandedBytes { get; set; }

        public HashSet<string> Directories { get; } = new(StringComparer.OrdinalIgnoreCase);

        public HashSet<string> ExplicitPaths { get; } = new(StringComparer.OrdinalIgnoreCase);

        public HashSet<string> Files { get; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private sealed class BoundedHashingWriteStream(Stream inner, long maximumBytes) : Stream
    {
        private readonly IncrementalHash _hash = IncrementalHash.CreateHash(
            HashAlgorithmName.SHA256
        );

        public long BytesWritten { get; private set; }

        public override bool CanRead => false;

        public override bool CanSeek => false;

        public override bool CanWrite => true;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => BytesWritten;
            set => throw new NotSupportedException();
        }

        public override void Flush() => inner.Flush();

        public override Task FlushAsync(CancellationToken cancellationToken) =>
            inner.FlushAsync(cancellationToken);

        public override int Read(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) =>
            Write(buffer.AsSpan(offset, count));

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            EnsureCapacity(buffer.Length);
            inner.Write(buffer);
            _hash.AppendData(buffer);
            BytesWritten += buffer.Length;
        }

        public override async ValueTask WriteAsync(
            ReadOnlyMemory<byte> buffer,
            CancellationToken cancellationToken = default
        )
        {
            EnsureCapacity(buffer.Length);
            await inner.WriteAsync(buffer, cancellationToken);
            _hash.AppendData(buffer.Span);
            BytesWritten += buffer.Length;
        }

        public override async Task WriteAsync(
            byte[] buffer,
            int offset,
            int count,
            CancellationToken cancellationToken
        ) => await WriteAsync(buffer.AsMemory(offset, count), cancellationToken);

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _hash.Dispose();
            }

            base.Dispose(disposing);
        }

        private void EnsureCapacity(int count)
        {
            if (count < 0 || BytesWritten > maximumBytes - count)
            {
                throw new InvalidDataException(
                    "OCI pack archive exceeds the 512 MiB download limit."
                );
            }
        }

        public string GetDigest() =>
            $"sha256:{Convert.ToHexString(_hash.GetHashAndReset()).ToLowerInvariant()}";
    }
}
