using System.Diagnostics;
using System.IO.Abstractions;
using System.Security;
using System.Text;
using System.Text.Json;
using Lunapack.Cli.Application.CommandExecution;
using Lunapack.Cli.Application.Serialization;
using Lunapack.Cli.Trust;

namespace Lunapack.Cli.Packs.Lifecycle;

internal sealed class LifecycleHookHistoryStore(IFileSystem fileSystem, string settingsDirectory)
{
    public const string FileName = "lifecycle-hook-history.jsonl";
    public const int MaximumEntryCount = 100;

    private const int MaximumArgumentsPerEntry = 1024;
    private const int MaximumTextLength = 32 * 1024;
    private const long MaximumFileBytes = 8L * 1024 * 1024;
    private const int LockTimeoutMilliseconds = 2000;
    private const int LockRetryDelayMilliseconds = 20;
    private const string LockFileName = ".lifecycle-hook-history.lock";
    private static readonly UTF8Encoding _utf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true
    );
    private static readonly string[] _allowedEvents =
    [
        "preInstall",
        "postInstall",
        "preUpdate",
        "postUpdate",
        "preUninstall",
        "postUninstall",
    ];
    private static readonly string[] _allowedOutcomes =
    [
        "succeeded",
        "failed",
        "canceled",
        "start-failed",
    ];
    private static readonly string[] _allowedAuthorizationSources =
    [
        "scripts-run",
        "persisted-trust",
        "interactive-confirmation",
    ];
    private static readonly string[] _allowedTrustScopes = ["project", "local-user", "global-user"];

    private readonly string _settingsDirectory = fileSystem.Path.GetFullPath(settingsDirectory);

    public string HistoryPath { get; } =
        fileSystem.Path.Combine(fileSystem.Path.GetFullPath(settingsDirectory), FileName);

    public Task<ManifestOperationResult<IReadOnlyList<LifecycleHookHistoryEntry>>> LoadAsync()
    {
        try
        {
            var pathError = ValidateExistingPaths(includeLock: false);
            if (pathError is not null)
            {
                return Result(
                    ManifestOperationResult<IReadOnlyList<LifecycleHookHistoryEntry>>.Failure(
                        pathError
                    )
                );
            }

            return fileSystem.File.Exists(HistoryPath)
                ? Task.FromResult(ReadEntries())
                : Result(
                    ManifestOperationResult<IReadOnlyList<LifecycleHookHistoryEntry>>.Success([])
                );
        }
        catch (Exception exception)
            when (exception
                    is IOException
                        or UnauthorizedAccessException
                        or SecurityException
                        or ArgumentException
                        or NotSupportedException
            )
        {
            return Result(
                ManifestOperationResult<IReadOnlyList<LifecycleHookHistoryEntry>>.Failure(
                    $"Unable to read lifecycle hook history: {exception.Message}"
                )
            );
        }
    }

    public async Task<ManifestOperationResult<bool>> AppendAsync(LifecycleHookHistoryEntry entry)
    {
        if (!IsValidEntry(entry))
        {
            return ManifestOperationResult<bool>.Failure(
                "Refusing to persist an invalid lifecycle hook history entry."
            );
        }

        var sanitizedEntry = entry with
        {
            Arguments = LifecycleHookArgumentRedactor.Redact(entry.Arguments),
        };
        if (!IsValidEntry(sanitizedEntry))
        {
            return ManifestOperationResult<bool>.Failure(
                "Refusing to persist an invalid lifecycle hook history entry."
            );
        }

        try
        {
            var pathError = ValidateExistingPaths(includeLock: false);
            if (pathError is not null)
            {
                return ManifestOperationResult<bool>.Failure(pathError);
            }

            EnsureSettingsDirectory();
            var acquiredLock = await AcquireLockAsync();
            if (acquiredLock.Value is not { } stream)
            {
                return ManifestOperationResult<bool>.Failure(
                    acquiredLock.Error ?? "Unable to lock lifecycle hook history."
                );
            }

            using (stream)
            {
                var lockedPathError = ValidateExistingPaths(includeLock: true);
                if (lockedPathError is not null)
                {
                    return ManifestOperationResult<bool>.Failure(lockedPathError);
                }

                var existing = fileSystem.File.Exists(HistoryPath)
                    ? ReadEntries()
                    : ManifestOperationResult<IReadOnlyList<LifecycleHookHistoryEntry>>.Success([]);
                if (existing.Value is not { } entries)
                {
                    return ManifestOperationResult<bool>.Failure(
                        existing.Error ?? "Unable to read lifecycle hook history."
                    );
                }

                var retained = entries.Append(sanitizedEntry).TakeLast(MaximumEntryCount).ToArray();
                return WriteEntries(retained);
            }
        }
        catch (Exception exception)
            when (exception
                    is IOException
                        or UnauthorizedAccessException
                        or SecurityException
                        or ArgumentException
                        or NotSupportedException
                        or JsonException
            )
        {
            return ManifestOperationResult<bool>.Failure(
                $"Unable to write lifecycle hook history: {exception.Message}"
            );
        }
    }

    private ManifestOperationResult<IReadOnlyList<LifecycleHookHistoryEntry>> ReadEntries()
    {
        try
        {
            using var stream = fileSystem.File.Open(
                HistoryPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete
            );
            if (stream.Length > MaximumFileBytes)
            {
                return ManifestOperationResult<IReadOnlyList<LifecycleHookHistoryEntry>>.Failure(
                    $"Lifecycle hook history in '{HistoryPath}' is too large."
                );
            }

            using var reader = new StreamReader(
                stream,
                _utf8,
                detectEncodingFromByteOrderMarks: false,
                bufferSize: 1024,
                leaveOpen: false
            );
            var entries = new List<LifecycleHookHistoryEntry>(MaximumEntryCount);
            var lineNumber = 0;
            while (reader.ReadLine() is { } line)
            {
                lineNumber++;
                if (string.IsNullOrWhiteSpace(line) || entries.Count >= MaximumEntryCount)
                {
                    return InvalidHistory($"line {lineNumber}");
                }

                var entry = JsonSerializer.Deserialize(
                    line,
                    LunapackJsonContext.Default.LifecycleHookHistoryEntry
                );
                if (entry is null || !IsValidEntry(entry))
                {
                    return InvalidHistory($"line {lineNumber}");
                }

                entries.Add(entry);
            }

            return ManifestOperationResult<IReadOnlyList<LifecycleHookHistoryEntry>>.Success(
                entries
            );
        }
        catch (Exception exception)
            when (exception
                    is IOException
                        or UnauthorizedAccessException
                        or DecoderFallbackException
                        or JsonException
            )
        {
            return ManifestOperationResult<IReadOnlyList<LifecycleHookHistoryEntry>>.Failure(
                $"Unable to read lifecycle hook history: {exception.Message}"
            );
        }
    }

    private ManifestOperationResult<bool> WriteEntries(
        IReadOnlyList<LifecycleHookHistoryEntry> entries
    )
    {
        var temporaryPath = fileSystem.Path.Combine(
            _settingsDirectory,
            $".{FileName}.{Guid.NewGuid():N}.tmp"
        );
        try
        {
            var content =
                string.Join(
                    Environment.NewLine,
                    entries.Select(entry =>
                        JsonSerializer.Serialize(
                            entry,
                            LunapackJsonContext.Default.LifecycleHookHistoryEntry
                        )
                    )
                ) + Environment.NewLine;
            if (_utf8.GetByteCount(content) > MaximumFileBytes)
            {
                return ManifestOperationResult<bool>.Failure(
                    $"Lifecycle hook history in '{HistoryPath}' exceeds its safe size limit."
                );
            }

            fileSystem.File.WriteAllText(temporaryPath, content, _utf8);
            UserSettingsPathSecurity.Apply(temporaryPath, directory: false);
            fileSystem.File.Move(temporaryPath, HistoryPath, overwrite: true);
            return ManifestOperationResult<bool>.Success(true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return ManifestOperationResult<bool>.Failure(
                $"Unable to write lifecycle hook history: {exception.Message}"
            );
        }
        finally
        {
            DeleteTemporaryFile(temporaryPath);
        }
    }

    private async Task<ManifestOperationResult<Stream>> AcquireLockAsync()
    {
        var lockPath = fileSystem.Path.Combine(_settingsDirectory, LockFileName);
        var startedAt = Stopwatch.StartNew();
        IOException? lastError = null;
        while (startedAt.ElapsedMilliseconds < LockTimeoutMilliseconds)
        {
            try
            {
                var existed = fileSystem.File.Exists(lockPath);
                if (fileSystem.Directory.Exists(lockPath))
                {
                    return ManifestOperationResult<Stream>.Failure(
                        $"Lifecycle hook history lock path '{lockPath}' must be a regular file."
                    );
                }

                if (existed)
                {
                    var pathError = UserSettingsPathSecurity.ValidateExisting(
                        fileSystem,
                        lockPath,
                        directory: false
                    );
                    if (pathError is not null)
                    {
                        return ManifestOperationResult<Stream>.Failure(pathError);
                    }
                }

                var stream = fileSystem.File.Open(
                    lockPath,
                    FileMode.OpenOrCreate,
                    FileAccess.ReadWrite,
                    FileShare.None
                );
                var transferOwnership = false;
                try
                {
                    if (!existed)
                    {
                        UserSettingsPathSecurity.Apply(lockPath, directory: false);
                    }

                    transferOwnership = true;
                    return ManifestOperationResult<Stream>.Success(stream);
                }
                finally
                {
                    if (!transferOwnership)
                    {
                        stream.Dispose();
                    }
                }
            }
            catch (IOException exception)
            {
                lastError = exception;
                await Task.Delay(LockRetryDelayMilliseconds);
            }
            catch (UnauthorizedAccessException exception)
            {
                return ManifestOperationResult<Stream>.Failure(
                    $"Unable to lock lifecycle hook history: {exception.Message}"
                );
            }
        }

        return ManifestOperationResult<Stream>.Failure(
            $"Unable to lock lifecycle hook history: {lastError?.Message ?? "lock timed out."}"
        );
    }

    private string? ValidateExistingPaths(bool includeLock)
    {
        if (fileSystem.File.Exists(_settingsDirectory))
        {
            return $"User settings path '{_settingsDirectory}' must be a directory.";
        }

        if (!fileSystem.Directory.Exists(_settingsDirectory))
        {
            return null;
        }

        var directoryError = UserSettingsPathSecurity.ValidateExisting(
            fileSystem,
            _settingsDirectory,
            directory: true
        );
        if (directoryError is not null)
        {
            return directoryError;
        }

        if (fileSystem.Directory.Exists(HistoryPath))
        {
            return $"Lifecycle hook history path '{HistoryPath}' must be a regular file.";
        }

        if (
            fileSystem.File.Exists(HistoryPath)
            && UserSettingsPathSecurity.ValidateExisting(fileSystem, HistoryPath, directory: false)
                is { } historyError
        )
        {
            return historyError;
        }

        if (!includeLock)
        {
            return null;
        }

        var lockPath = fileSystem.Path.Combine(_settingsDirectory, LockFileName);
        if (fileSystem.Directory.Exists(lockPath))
        {
            return $"Lifecycle hook history lock path '{lockPath}' must be a regular file.";
        }

        return fileSystem.File.Exists(lockPath)
            ? UserSettingsPathSecurity.ValidateExisting(fileSystem, lockPath, directory: false)
            : null;
    }

    private void EnsureSettingsDirectory()
    {
        if (fileSystem.Directory.Exists(_settingsDirectory))
        {
            return;
        }

        fileSystem.Directory.CreateDirectory(_settingsDirectory);
        UserSettingsPathSecurity.Apply(_settingsDirectory, directory: true);
        var error = UserSettingsPathSecurity.ValidateExisting(
            fileSystem,
            _settingsDirectory,
            directory: true
        );
        if (error is not null)
        {
            throw new IOException(error);
        }
    }

    private void DeleteTemporaryFile(string path)
    {
        try
        {
            if (fileSystem.File.Exists(path))
            {
                fileSystem.File.Delete(path);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // The original write result remains authoritative when temporary cleanup also fails.
        }
    }

    private ManifestOperationResult<IReadOnlyList<LifecycleHookHistoryEntry>> InvalidHistory(
        string detail
    ) =>
        ManifestOperationResult<IReadOnlyList<LifecycleHookHistoryEntry>>.Failure(
            $"Invalid lifecycle hook history in '{HistoryPath}' at {detail}."
        );

    private static bool IsValidEntry(LifecycleHookHistoryEntry entry)
    {
        if (
            entry.Version != LifecycleHookHistoryEntry.CurrentVersion
            || entry.Timestamp.Offset != TimeSpan.Zero
            || string.IsNullOrWhiteSpace(entry.Event)
            || !_allowedEvents.Contains(entry.Event, StringComparer.Ordinal)
            || !IsSafeText(entry.Executable)
            || !IsSafeText(entry.PackId)
            || !IsSafeText(entry.PackVersion)
            || entry.Arguments is null
            || entry.Arguments.Count > MaximumArgumentsPerEntry
            || entry.Arguments.Any(argument => !IsSafeArgument(argument))
            || !_allowedAuthorizationSources.Contains(
                entry.AuthorizationSource,
                StringComparer.Ordinal
            )
            || entry.TrustScopes is null
            || entry.TrustScopes.Count > _allowedTrustScopes.Length
            || entry.TrustScopes.Any(scope =>
                !_allowedTrustScopes.Contains(scope, StringComparer.Ordinal)
            )
            || entry.TrustScopes.Distinct(StringComparer.Ordinal).Count() != entry.TrustScopes.Count
            || entry.DurationMilliseconds < 0
            || !_allowedOutcomes.Contains(entry.Outcome, StringComparer.Ordinal)
        )
        {
            return false;
        }

        var hasPersistedTrust = string.Equals(
            entry.AuthorizationSource,
            "persisted-trust",
            StringComparison.Ordinal
        );
        if (
            (hasPersistedTrust && entry.TrustScopes.Count == 0)
            || (!hasPersistedTrust && entry.TrustScopes.Count > 0)
        )
        {
            return false;
        }

        return entry.Outcome switch
        {
            "succeeded" => entry.ExitCode == 0,
            "failed" => entry.ExitCode is not null and not 0,
            "start-failed" => entry.ExitCode is null,
            "canceled" => true,
            _ => false,
        };
    }

    private static bool IsSafeText(string? value) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= MaximumTextLength;

    private static bool IsSafeArgument(string? value) =>
        value is not null && value.Length <= MaximumTextLength;

    private static Task<ManifestOperationResult<T>> Result<T>(ManifestOperationResult<T> result) =>
        Task.FromResult(result);
}
