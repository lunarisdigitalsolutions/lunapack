using System.Buffers.Binary;
using System.IO.Abstractions;
using System.IO.Compression;
using System.Security.Cryptography;
using Lunapack.Cli.Application.CommandExecution;
using Lunapack.Cli.Application.Paths;
using Lunapack.Cli.Application.Preferences;

namespace Lunapack.Cli.Sources.Oci;

internal static class OciPackArchive
{
    public const int MaximumEntryCount = 100_000;
    public const long MaximumArchiveBytes = 512L * 1024 * 1024;
    public const long MaximumEntryBytes = 512L * 1024 * 1024;
    public const long MaximumExpandedBytes = 2L * 1024 * 1024 * 1024;

    public sealed record Limits(
        int MaximumEntryCount,
        long MaximumArchiveBytes,
        long MaximumEntryBytes,
        long MaximumExpandedBytes,
        int? MaximumFileCount = null,
        long? MaximumSnapshotBytes = null
    )
    {
        public static Limits Default { get; } =
            new(
                OciPackArchive.MaximumEntryCount,
                OciPackArchive.MaximumArchiveBytes,
                OciPackArchive.MaximumEntryBytes,
                OciPackArchive.MaximumExpandedBytes,
                (int)ResourceLimits.Default.FilesPerPack,
                ResourceLimits.Default.PackSnapshotBytes
            );

        public int EffectiveMaximumFileCount => MaximumFileCount ?? int.MaxValue;

        public long EffectiveMaximumSnapshotBytes => MaximumSnapshotBytes ?? MaximumExpandedBytes;

        public static Limits From(ResourceLimits resourceLimits) =>
            new(
                checked((int)resourceLimits.OciArchiveEntries),
                resourceLimits.OciDownloadBytes,
                resourceLimits.OciEntryBytes,
                resourceLimits.OciExpandedBytes,
                checked((int)resourceLimits.FilesPerPack),
                resourceLimits.PackSnapshotBytes
            );
    }

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
        CancellationToken cancellationToken,
        Limits? limits = null
    )
    {
        var archiveLimits = limits ?? Limits.Default;
        var sourceEntries = EnumerateSourceEntries(fileSystem, packDirectory, archiveLimits);
        if (sourceEntries.Value is not { } entries)
        {
            return ManifestOperationResult<OciBlobTransfer>.Failure(
                sourceEntries.Error ?? "Unable to enumerate pack content.",
                sourceEntries.ErrorKind
            );
        }

        if (entries.Count > archiveLimits.MaximumEntryCount)
        {
            return ManifestOperationResult<OciBlobTransfer>.Failure(
                $"Pack snapshot contains {entries.Count} archive entries, exceeding its {archiveLimits.MaximumEntryCount} entry limit.",
                ManifestOperationErrorKind.ResourceLimitExceeded
            );
        }

        using var output = new BoundedHashingWriteStream(
            destination,
            archiveLimits.MaximumArchiveBytes
        );
        try
        {
            await WriteArchiveAsync(fileSystem, output, entries, archiveLimits, cancellationToken);

            return ManifestOperationResult<OciBlobTransfer>.Success(
                new OciBlobTransfer(output.BytesWritten, output.GetDigest())
            );
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (OciResourceLimitException exception)
        {
            return ManifestOperationResult<OciBlobTransfer>.Failure(
                exception.Message,
                ManifestOperationErrorKind.ResourceLimitExceeded
            );
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

    private static async Task WriteArchiveAsync(
        IFileSystem fileSystem,
        Stream output,
        IReadOnlyList<SourceEntry> entries,
        Limits limits,
        CancellationToken cancellationToken
    )
    {
        long expandedBytes = 0;
        using var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true);
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
                limits.MaximumEntryBytes,
                Math.Min(limits.MaximumExpandedBytes, limits.EffectiveMaximumSnapshotBytes),
                expandedBytes,
                sourceEntry.RelativePath,
                cancellationToken
            );
            expandedBytes += sourceEntry.Length;
        }
    }

    public static async Task<ManifestOperationResult<IReadOnlyList<string>>> ExtractAsync(
        IFileSystem fileSystem,
        Stream archiveStream,
        string extractionRoot,
        CancellationToken cancellationToken,
        Limits? limits = null
    )
    {
        var archiveLimits = limits ?? Limits.Default;
        try
        {
            var entryBudget = ValidateArchiveEntryBudget(archiveStream, archiveLimits);
            if (!entryBudget.IsSuccess)
            {
                return ManifestOperationResult<IReadOnlyList<string>>.Failure(
                    entryBudget.Error!,
                    entryBudget.ErrorKind
                );
            }

            using var archive = new ZipArchive(archiveStream, ZipArchiveMode.Read, leaveOpen: true);
            var validated = ValidateEntries(archive, archiveLimits);
            if (validated.Value is not { } preparedEntries)
            {
                return ManifestOperationResult<IReadOnlyList<string>>.Failure(
                    validated.Error ?? "OCI pack archive contains unsafe entries.",
                    validated.ErrorKind
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
                    preparedEntries,
                    extractionRoot,
                    archiveLimits,
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
        catch (OciResourceLimitException exception)
        {
            var cleanupError = DeleteExtractionRoot(fileSystem, extractionRoot);
            return ManifestOperationResult<IReadOnlyList<string>>.Failure(
                cleanupError is null
                    ? exception.Message
                    : $"{exception.Message} Temporary cleanup also failed: {cleanupError}",
                ManifestOperationErrorKind.ResourceLimitExceeded
            );
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

    private static ManifestOperationResult<bool> ValidateArchiveEntryBudget(
        Stream archiveStream,
        Limits limits
    )
    {
        if (!archiveStream.CanSeek)
        {
            return ManifestOperationResult<bool>.Success(true);
        }

        var originalPosition = archiveStream.Position;
        try
        {
            if (
                !TryReadCentralDirectoryMetadata(
                    archiveStream,
                    out var declaredEntries,
                    out var centralDirectoryOffset,
                    out var centralDirectorySize
                )
            )
            {
                return ManifestOperationResult<bool>.Success(true);
            }

            var declaredBudget = ValidateArchiveEntryCount(declaredEntries, limits, "declares");
            if (!declaredBudget.IsSuccess)
            {
                return declaredBudget;
            }

            archiveStream.Position = checked((long)centralDirectoryOffset);
            var remaining = centralDirectorySize;
            ulong observedEntries = 0;
            Span<byte> header = stackalloc byte[46];
            while (remaining > 0)
            {
                if (remaining < 46)
                {
                    return ManifestOperationResult<bool>.Success(true);
                }

                archiveStream.ReadExactly(header);
                remaining -= 46;
                if (BinaryPrimitives.ReadUInt32LittleEndian(header) != 0x02014b50)
                {
                    return ManifestOperationResult<bool>.Success(true);
                }

                var nameLength = BinaryPrimitives.ReadUInt16LittleEndian(header[28..]);
                var extraLength = BinaryPrimitives.ReadUInt16LittleEndian(header[30..]);
                var commentLength = BinaryPrimitives.ReadUInt16LittleEndian(header[32..]);
                var variableLength = checked((ulong)nameLength + extraLength + commentLength);
                if (variableLength > remaining)
                {
                    return ManifestOperationResult<bool>.Success(true);
                }

                archiveStream.Position = checked(archiveStream.Position + (long)variableLength);
                remaining -= variableLength;
                observedEntries++;
                var observedBudget = ValidateArchiveEntryCount(observedEntries, limits, "contains");
                if (!observedBudget.IsSuccess)
                {
                    return observedBudget;
                }
            }

            return ManifestOperationResult<bool>.Success(true);
        }
        finally
        {
            archiveStream.Position = originalPosition;
        }
    }

    private static ManifestOperationResult<bool> ValidateArchiveEntryCount(
        ulong entryCount,
        Limits limits,
        string description
    )
    {
        if (entryCount > (ulong)limits.MaximumEntryCount)
        {
            return ManifestOperationResult<bool>.Failure(
                $"OCI pack archive {description} at least {entryCount} entries, exceeding its {limits.MaximumEntryCount} entry limit.",
                ManifestOperationErrorKind.ResourceLimitExceeded
            );
        }

        return entryCount > (ulong)limits.EffectiveMaximumFileCount
            ? ManifestOperationResult<bool>.Failure(
                $"OCI pack archive {description} at least {entryCount} files and directories, exceeding its {limits.EffectiveMaximumFileCount} file and directory limit.",
                ManifestOperationErrorKind.ResourceLimitExceeded
            )
            : ManifestOperationResult<bool>.Success(true);
    }

    private static bool TryReadCentralDirectoryMetadata(
        Stream archiveStream,
        out ulong entryCount,
        out ulong offset,
        out ulong size
    )
    {
        entryCount = 0;
        offset = 0;
        size = 0;
        var length = archiveStream.Length;
        if (length < 22)
        {
            return false;
        }

        const int maximumTrailerBytes = 65_557;
        var trailerLength = checked((int)Math.Min(length, maximumTrailerBytes));
        archiveStream.Position = length - trailerLength;
        var trailer = new byte[trailerLength];
        archiveStream.ReadExactly(trailer);
        for (var endIndex = trailer.Length - 22; endIndex >= 0; endIndex--)
        {
            var span = trailer.AsSpan(endIndex);
            if (BinaryPrimitives.ReadUInt32LittleEndian(span) != 0x06054B50)
            {
                continue;
            }

            var commentLength = BinaryPrimitives.ReadUInt16LittleEndian(span[20..]);
            if (endIndex + 22 + commentLength != trailer.Length)
            {
                continue;
            }

            entryCount = BinaryPrimitives.ReadUInt16LittleEndian(span[10..]);
            size = BinaryPrimitives.ReadUInt32LittleEndian(span[12..]);
            offset = BinaryPrimitives.ReadUInt32LittleEndian(span[16..]);
            var archiveEndPosition = length - trailerLength + endIndex;
            if (entryCount != ushort.MaxValue && size != uint.MaxValue && offset != uint.MaxValue)
            {
                return offset <= (ulong)length && size <= (ulong)length - offset;
            }

            return TryReadZip64CentralDirectoryMetadata(
                archiveStream,
                archiveEndPosition,
                out entryCount,
                out offset,
                out size
            );
        }

        return false;
    }

    private static bool TryReadZip64CentralDirectoryMetadata(
        Stream archiveStream,
        long endPosition,
        out ulong entryCount,
        out ulong offset,
        out ulong size
    )
    {
        entryCount = 0;
        offset = 0;
        size = 0;
        if (endPosition < 20)
        {
            return false;
        }

        archiveStream.Position = endPosition - 20;
        Span<byte> locator = stackalloc byte[20];
        archiveStream.ReadExactly(locator);
        if (BinaryPrimitives.ReadUInt32LittleEndian(locator) != 0x07064B50)
        {
            return false;
        }

        var recordOffset = BinaryPrimitives.ReadUInt64LittleEndian(locator[8..]);
        if (recordOffset > (ulong)archiveStream.Length - 56)
        {
            return false;
        }

        archiveStream.Position = checked((long)recordOffset);
        Span<byte> record = stackalloc byte[56];
        archiveStream.ReadExactly(record);
        if (BinaryPrimitives.ReadUInt32LittleEndian(record) != 0x06064B50)
        {
            return false;
        }

        entryCount = BinaryPrimitives.ReadUInt64LittleEndian(record[32..]);
        size = BinaryPrimitives.ReadUInt64LittleEndian(record[40..]);
        offset = BinaryPrimitives.ReadUInt64LittleEndian(record[48..]);
        return offset <= (ulong)archiveStream.Length
            && size <= (ulong)archiveStream.Length - offset;
    }

    private static async Task<IReadOnlyList<string>> ExtractEntriesAsync(
        IFileSystem fileSystem,
        IReadOnlyList<ValidatedEntry> entries,
        string extractionRoot,
        Limits limits,
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
                limits.MaximumEntryBytes,
                Math.Min(limits.MaximumExpandedBytes, limits.EffectiveMaximumSnapshotBytes),
                expandedBytes,
                entry.RelativePath,
                cancellationToken
            );
        }

        return [.. entries.Where(entry => !entry.IsDirectory).Select(entry => entry.RelativePath)];
    }

    private static ManifestOperationResult<List<SourceEntry>> EnumerateSourceEntries(
        IFileSystem fileSystem,
        string packDirectory,
        Limits limits
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
            var snapshotItemCount = 0;

            void Visit(string directory)
            {
                var siblings = GetBoundedSourceChildren(
                    fileSystem,
                    directory,
                    entries.Count,
                    limits.MaximumEntryCount,
                    limits.EffectiveMaximumFileCount
                );
                foreach (var item in siblings)
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
                    var observedEntryCount = entries.Count + 1;
                    if (observedEntryCount > limits.MaximumEntryCount)
                    {
                        throw new OciResourceLimitException(
                            $"Pack snapshot exceeds its {limits.MaximumEntryCount} archive entry limit (at least {observedEntryCount} entries observed)."
                        );
                    }

                    snapshotItemCount++;
                    if (snapshotItemCount > limits.EffectiveMaximumFileCount)
                    {
                        throw new OciResourceLimitException(
                            $"Pack snapshot exceeds its {limits.EffectiveMaximumFileCount} file limit (at least {snapshotItemCount} files and directories observed)."
                        );
                    }

                    long length = 0;
                    if (!isDirectory)
                    {
                        length = fileSystem.FileInfo.New(item.FullName).Length;
                        if (length < 0 || length > limits.MaximumEntryBytes)
                        {
                            throw new OciResourceLimitException(
                                $"Pack file '{canonicalPath}' exceeds its {limits.MaximumEntryBytes} byte limit (at least {length} bytes observed)."
                            );
                        }

                        expandedBytes = checked(expandedBytes + length);
                        var snapshotByteLimit = Math.Min(
                            limits.MaximumExpandedBytes,
                            limits.EffectiveMaximumSnapshotBytes
                        );
                        if (expandedBytes > snapshotByteLimit)
                        {
                            throw new OciResourceLimitException(
                                $"Pack snapshot exceeds its {snapshotByteLimit} byte limit (at least {expandedBytes} bytes observed)."
                            );
                        }
                    }

                    entries.Add(new SourceEntry(item.FullName, canonicalPath, isDirectory, length));
                    if (isDirectory)
                    {
                        Visit(item.FullName);
                    }
                }
            }

            Visit(root);
            return ManifestOperationResult<List<SourceEntry>>.Success([
                .. entries.OrderBy(entry => entry.RelativePath, StringComparer.Ordinal),
            ]);
        }
        catch (OciResourceLimitException exception)
        {
            return ManifestOperationResult<List<SourceEntry>>.Failure(
                exception.Message,
                ManifestOperationErrorKind.ResourceLimitExceeded
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

    private static List<IFileSystemInfo> GetBoundedSourceChildren(
        IFileSystem fileSystem,
        string directory,
        int acceptedEntryCount,
        int maximumEntryCount,
        int maximumSnapshotItemCount
    )
    {
        var siblings = new List<IFileSystemInfo>();
        foreach (var item in fileSystem.DirectoryInfo.New(directory).EnumerateFileSystemInfos())
        {
            var observedEntryCount = acceptedEntryCount + siblings.Count + 1;
            if (observedEntryCount > maximumSnapshotItemCount)
            {
                throw new OciResourceLimitException(
                    $"Pack snapshot exceeds its {maximumSnapshotItemCount} file and directory limit (at least {observedEntryCount} entries observed)."
                );
            }

            if (observedEntryCount > maximumEntryCount)
            {
                throw new OciResourceLimitException(
                    $"Pack snapshot exceeds its {maximumEntryCount} archive entry limit (at least {observedEntryCount} entries observed)."
                );
            }

            siblings.Add(item);
        }

        siblings.Sort((left, right) => StringComparer.Ordinal.Compare(left.Name, right.Name));
        return siblings;
    }

    private static ManifestOperationResult<IReadOnlyList<ValidatedEntry>> ValidateEntries(
        ZipArchive archive,
        Limits limits
    )
    {
        if (archive.Entries.Count > limits.MaximumEntryCount)
        {
            return ManifestOperationResult<IReadOnlyList<ValidatedEntry>>.Failure(
                $"OCI pack archive contains {archive.Entries.Count} entries, exceeding its {limits.MaximumEntryCount} entry limit.",
                ManifestOperationErrorKind.ResourceLimitExceeded
            );
        }

        var entries = new List<ValidatedEntry>(archive.Entries.Count);
        var state = new EntryValidationState();
        foreach (var entry in archive.Entries)
        {
            var validation = ValidateEntry(entry, state, limits);
            if (validation.Value is not { } validatedEntry)
            {
                return ManifestOperationResult<IReadOnlyList<ValidatedEntry>>.Failure(
                    validation.Error ?? "OCI pack archive contains an unsafe entry.",
                    validation.ErrorKind
                );
            }

            entries.Add(validatedEntry);
        }

        return ManifestOperationResult<IReadOnlyList<ValidatedEntry>>.Success(entries);
    }

    private static ManifestOperationResult<ValidatedEntry> ValidateEntry(
        ZipArchiveEntry entry,
        EntryValidationState state,
        Limits limits
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
        if (entryLength < 0 || entryLength > limits.MaximumEntryBytes)
        {
            return ManifestOperationResult<ValidatedEntry>.Failure(
                $"OCI pack archive entry '{relativePath}' exceeds its {limits.MaximumEntryBytes} byte limit (at least {entryLength} bytes declared).",
                ManifestOperationErrorKind.ResourceLimitExceeded
            );
        }

        var validation = isDirectory
            ? ValidateDirectory(relativePath, entryLength, state, limits)
            : ValidateFile(relativePath, entryLength, state, limits);
        return validation.IsSuccess
            ? ManifestOperationResult<ValidatedEntry>.Success(
                new ValidatedEntry(entry, relativePath, isDirectory, entryLength)
            )
            : ManifestOperationResult<ValidatedEntry>.Failure(
                validation.Error ?? "OCI pack archive contains conflicting entries.",
                validation.ErrorKind
            );
    }

    private static ManifestOperationResult<bool> ValidateDirectory(
        string relativePath,
        long entryLength,
        EntryValidationState state,
        Limits limits
    )
    {
        if (entryLength != 0 || state.Files.Contains(relativePath))
        {
            return ManifestOperationResult<bool>.Failure(
                $"OCI pack archive has conflicting file and directory paths at '{relativePath}'."
            );
        }

        var registered = RegisterDirectory(relativePath, state, limits);
        return registered.IsSuccess
            ? RegisterParentDirectories(relativePath, state, limits)
            : registered;
    }

    private static ManifestOperationResult<bool> ValidateFile(
        string relativePath,
        long entryLength,
        EntryValidationState state,
        Limits limits
    )
    {
        if (state.Directories.Contains(relativePath))
        {
            return ManifestOperationResult<bool>.Failure(
                $"OCI pack archive has conflicting file and directory paths at '{relativePath}'."
            );
        }

        state.Files.Add(relativePath);
        state.DeclaredSnapshotItemCount++;
        if (state.DeclaredSnapshotItemCount > limits.EffectiveMaximumFileCount)
        {
            return ManifestOperationResult<bool>.Failure(
                $"OCI pack archive exceeds its {limits.EffectiveMaximumFileCount} file limit (at least {state.DeclaredSnapshotItemCount} files and directories declared).",
                ManifestOperationErrorKind.ResourceLimitExceeded
            );
        }

        state.DeclaredExpandedBytes = checked(state.DeclaredExpandedBytes + entryLength);
        if (state.DeclaredExpandedBytes > limits.MaximumExpandedBytes)
        {
            return ManifestOperationResult<bool>.Failure(
                $"OCI pack archive exceeds its {limits.MaximumExpandedBytes} expanded byte limit (at least {state.DeclaredExpandedBytes} bytes declared).",
                ManifestOperationErrorKind.ResourceLimitExceeded
            );
        }

        if (state.DeclaredExpandedBytes > limits.EffectiveMaximumSnapshotBytes)
        {
            return ManifestOperationResult<bool>.Failure(
                $"OCI pack snapshot exceeds its {limits.EffectiveMaximumSnapshotBytes} byte limit (at least {state.DeclaredExpandedBytes} bytes declared).",
                ManifestOperationErrorKind.ResourceLimitExceeded
            );
        }

        return RegisterParentDirectories(relativePath, state, limits);
    }

    private static ManifestOperationResult<bool> RegisterParentDirectories(
        string relativePath,
        EntryValidationState state,
        Limits limits
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

            var registered = RegisterDirectory(parent, state, limits);
            if (!registered.IsSuccess)
            {
                return registered;
            }

            separatorIndex = parent.LastIndexOf('/');
        }

        return ManifestOperationResult<bool>.Success(true);
    }

    private static ManifestOperationResult<bool> RegisterDirectory(
        string relativePath,
        EntryValidationState state,
        Limits limits
    )
    {
        if (!state.Directories.Add(relativePath))
        {
            return ManifestOperationResult<bool>.Success(true);
        }

        state.DeclaredSnapshotItemCount++;
        return state.DeclaredSnapshotItemCount > limits.EffectiveMaximumFileCount
            ? ManifestOperationResult<bool>.Failure(
                $"OCI pack archive exceeds its {limits.EffectiveMaximumFileCount} file limit (at least {state.DeclaredSnapshotItemCount} files and directories declared).",
                ManifestOperationErrorKind.ResourceLimitExceeded
            )
            : ManifestOperationResult<bool>.Success(true);
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
        long maximumTotalBytes,
        long bytesPreviouslyCopied,
        string relativePath,
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
            if (copied > maximumEntryBytes)
            {
                throw new OciResourceLimitException(
                    $"OCI pack archive entry '{relativePath}' exceeds its {maximumEntryBytes} byte limit (at least {copied} bytes copied)."
                );
            }

            var totalBytes = checked(bytesPreviouslyCopied + copied);
            if (totalBytes > maximumTotalBytes)
            {
                throw new OciResourceLimitException(
                    $"OCI pack snapshot exceeds its {maximumTotalBytes} byte limit (at least {totalBytes} bytes copied)."
                );
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

        public int DeclaredSnapshotItemCount { get; set; }

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
                throw new OciResourceLimitException(
                    $"OCI pack archive exceeds its {maximumBytes} byte download limit (at least {checked(BytesWritten + count)} bytes produced)."
                );
            }
        }

        public string GetDigest() =>
            $"sha256:{Convert.ToHexString(_hash.GetHashAndReset()).ToLowerInvariant()}";
    }

    private sealed class OciResourceLimitException(string message) : Exception(message);
}
