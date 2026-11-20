using System.IO.Abstractions;
using Lunapack.Cli.Application.CommandExecution;
using Lunapack.Cli.Application.Preferences;
using Lunapack.Cli.Application.Serialization;
using Lunapack.Cli.Packs.Authoring;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Lunapack.Cli.Packs.Manifest;

internal sealed class PackManifestStore(IFileSystem fileSystem)
{
    public const string FileName = "pack.yml";

    private static readonly IDeserializer _deserializer = new StaticDeserializerBuilder(
        new LunapackYamlContext()
    )
        .IgnoreUnmatchedProperties()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .WithTypeConverter(new PackParameterYamlTypeConverter())
        .WithTypeConverter(new ScalarValueDictionaryYamlTypeConverter())
        .Build();

    private static readonly ISerializer _serializer = new StaticSerializerBuilder(
        new LunapackYamlContext()
    )
        .ConfigureDefaultValuesHandling(DefaultValuesHandling.OmitNull)
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .WithTypeConverter(new PackParameterYamlTypeConverter())
        .WithTypeConverter(new ScalarValueDictionaryYamlTypeConverter())
        .Build();

    public Task<ManifestOperationResult<PackManifest>> CreateAsync(
        string projectDirectory,
        PackManifest manifest
    ) => CreateAsync(projectDirectory, manifest, ResourceLimits.Default.PackManifestBytes);

    public Task<ManifestOperationResult<PackManifest>> CreateAsync(
        string projectDirectory,
        PackManifest manifest,
        long byteLimit
    ) =>
        WithWriteLockAsync(
            projectDirectory,
            () => CreateCoreAsync(projectDirectory, manifest, byteLimit)
        );

    private Task<ManifestOperationResult<PackManifest>> CreateCoreAsync(
        string projectDirectory,
        PackManifest manifest,
        long byteLimit
    )
    {
        var manifestPath = GetManifestPath(projectDirectory);
        if (fileSystem.File.Exists(manifestPath))
        {
            return Task.FromResult(
                ManifestOperationResult<PackManifest>.Failure(
                    $"{FileName} already exists in '{projectDirectory}'."
                )
            );
        }

        return WriteAsync(
            projectDirectory,
            PackManifestPathNormalizer.Normalize(manifest),
            null,
            byteLimit,
            initialization: true
        );
    }

    public Task<ManifestOperationResult<PackManifest>> LoadAsync(string projectDirectory) =>
        LoadAsync(projectDirectory, ResourceLimits.Default.PackManifestBytes);

    public async Task<ManifestOperationResult<PackManifest>> LoadAsync(
        string projectDirectory,
        long byteLimit
    )
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(byteLimit);
        var manifestPath = GetManifestPath(projectDirectory);
        if (!fileSystem.File.Exists(manifestPath))
        {
            return ManifestOperationResult<PackManifest>.Failure(
                $"Missing {FileName} in '{projectDirectory}'."
            );
        }

        try
        {
            var read = await BoundedFileReader.ReadTextAsync(
                fileSystem,
                manifestPath,
                byteLimit,
                FileName
            );
            if (read.Value is not { } contents)
            {
                return ManifestOperationResult<PackManifest>.Failure(
                    read.Error ?? $"Unable to read {FileName}.",
                    read.ErrorKind
                );
            }

            var manifest = _deserializer.Deserialize<PackManifest>(contents.Text);
            if (manifest is null)
            {
                return ManifestOperationResult<PackManifest>.Failure(
                    $"Invalid {FileName}: manifest is empty."
                );
            }

            var issues = ManifestModelValidator.Validate(manifest);
            return issues.Count == 0
                ? ManifestOperationResult<PackManifest>.Success(
                    PackManifestPathNormalizer.Normalize(manifest)
                )
                : ManifestOperationResult<PackManifest>.Failure(FormatIssues(issues));
        }
        catch (Exception exception)
            when (exception
                    is IOException
                        or UnauthorizedAccessException
                        or YamlDotNet.Core.YamlException
            )
        {
            return ManifestOperationResult<PackManifest>.Failure(
                $"Unable to read {FileName}: {exception.Message}"
            );
        }
    }

    public async Task<ManifestOperationResult<PackManifest>> UpdateAsync(
        string projectDirectory,
        Func<PackManifest, string?> mutation
    ) => await UpdateAsync(projectDirectory, mutation, ResourceLimits.Default.PackManifestBytes);

    public async Task<ManifestOperationResult<PackManifest>> UpdateAsync(
        string projectDirectory,
        Func<PackManifest, string?> mutation,
        long byteLimit
    ) =>
        await WithWriteLockAsync(
            projectDirectory,
            () => UpdateCoreAsync(projectDirectory, mutation, byteLimit)
        );

    private async Task<ManifestOperationResult<PackManifest>> UpdateCoreAsync(
        string projectDirectory,
        Func<PackManifest, string?> mutation,
        long byteLimit
    )
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(byteLimit);
        var manifestPath = GetManifestPath(projectDirectory);
        if (!fileSystem.File.Exists(manifestPath))
        {
            return ManifestOperationResult<PackManifest>.Failure(
                $"Missing {FileName} in '{projectDirectory}'."
            );
        }

        string originalContents;
        PackManifest manifest;
        try
        {
            var read = await BoundedFileReader.ReadTextAsync(
                fileSystem,
                manifestPath,
                byteLimit,
                FileName
            );
            if (read.Value is not { } contents)
            {
                return ManifestOperationResult<PackManifest>.Failure(
                    read.Error ?? $"Unable to read {FileName}.",
                    read.ErrorKind
                );
            }

            originalContents = contents.Text;
            manifest =
                _deserializer.Deserialize<PackManifest>(originalContents)
                ?? throw new YamlDotNet.Core.YamlException("Manifest is empty.");
        }
        catch (Exception exception)
            when (exception
                    is IOException
                        or UnauthorizedAccessException
                        or YamlDotNet.Core.YamlException
            )
        {
            return ManifestOperationResult<PackManifest>.Failure(
                $"Unable to read {FileName}: {exception.Message}"
            );
        }

        var mutationError = mutation(manifest);
        if (mutationError is not null)
        {
            return ManifestOperationResult<PackManifest>.Failure(mutationError);
        }

        return await WriteAsync(
            projectDirectory,
            PackManifestPathNormalizer.Normalize(manifest),
            originalContents,
            byteLimit
        );
    }

    private async Task<ManifestOperationResult<PackManifest>> WriteAsync(
        string projectDirectory,
        PackManifest manifest,
        string? expectedContents,
        long byteLimit,
        bool initialization = false
    )
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(byteLimit);
        var issues = ManifestModelValidator.Validate(manifest);
        if (issues.Count > 0)
        {
            return ManifestOperationResult<PackManifest>.Failure(FormatIssues(issues));
        }

        var manifestPath = GetManifestPath(projectDirectory);
        var temporaryPath = $"{manifestPath}.{Guid.NewGuid():N}.tmp";
        try
        {
            var serialized = Serialize(manifest, byteLimit, initialization);
            if (serialized.Value is not { } contents)
            {
                return ManifestOperationResult<PackManifest>.Failure(
                    serialized.Error!,
                    serialized.ErrorKind
                );
            }

            if (expectedContents is not null)
            {
                var unchanged = await VerifyExpectedContentsAsync(
                    manifestPath,
                    expectedContents,
                    byteLimit
                );
                if (!unchanged.IsSuccess)
                {
                    return ManifestOperationResult<PackManifest>.Failure(
                        unchanged.Error!,
                        unchanged.ErrorKind
                    );
                }
            }

            fileSystem.Directory.CreateDirectory(projectDirectory);
            fileSystem.File.WriteAllText(temporaryPath, contents);
            fileSystem.File.Move(
                temporaryPath,
                manifestPath,
                overwrite: expectedContents is not null
            );
            return ManifestOperationResult<PackManifest>.Success(manifest);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return ManifestOperationResult<PackManifest>.Failure(
                $"Unable to write {FileName}: {exception.Message}"
            );
        }
        finally
        {
            if (fileSystem.File.Exists(temporaryPath))
            {
                fileSystem.File.Delete(temporaryPath);
            }
        }
    }

    private static ManifestOperationResult<string> Serialize(
        PackManifest manifest,
        long byteLimit,
        bool initialization
    )
    {
        var contents = initialization
            ? _serializer.Serialize(
                new InitialPackManifest
                {
                    Author = manifest.Author,
                    Id = manifest.Id,
                    License = manifest.License,
                    Version = manifest.Version,
                }
            )
            : _serializer.Serialize(manifest);
        var serializedByteCount = System.Text.Encoding.UTF8.GetByteCount(contents);
        return serializedByteCount > byteLimit
            ? ManifestOperationResult<string>.Failure(
                $"{FileName} is {serializedByteCount} bytes, exceeding its {byteLimit} byte limit.",
                ManifestOperationErrorKind.ResourceLimitExceeded
            )
            : ManifestOperationResult<string>.Success(contents);
    }

    private async Task<ManifestOperationResult<bool>> VerifyExpectedContentsAsync(
        string manifestPath,
        string expectedContents,
        long byteLimit
    )
    {
        if (!fileSystem.File.Exists(manifestPath))
        {
            return ManifestOperationResult<bool>.Failure(
                $"{FileName} changed while the command was running."
            );
        }

        var current = await BoundedFileReader.ReadTextAsync(
            fileSystem,
            manifestPath,
            byteLimit,
            FileName
        );
        return
            current.Value is { } currentContents
            && string.Equals(currentContents.Text, expectedContents, StringComparison.Ordinal)
            ? ManifestOperationResult<bool>.Success(true)
            : ManifestOperationResult<bool>.Failure(
                current.Error ?? $"{FileName} changed while the command was running.",
                current.ErrorKind
            );
    }

    private string GetManifestPath(string projectDirectory) =>
        fileSystem.Path.Combine(projectDirectory, FileName);

    private async Task<ManifestOperationResult<PackManifest>> WithWriteLockAsync(
        string projectDirectory,
        Func<Task<ManifestOperationResult<PackManifest>>> action
    )
    {
        fileSystem.Directory.CreateDirectory(projectDirectory);
        var lockPath = fileSystem.Path.Combine(projectDirectory, $".{FileName}.lock");
        try
        {
            using var lockStream = fileSystem.File.Open(
                lockPath,
                FileMode.OpenOrCreate,
                FileAccess.ReadWrite,
                FileShare.None
            );
            return await action();
        }
        catch (IOException exception)
        {
            return ManifestOperationResult<PackManifest>.Failure(
                $"Unable to lock {FileName} for writing: {exception.Message}"
            );
        }
        catch (UnauthorizedAccessException exception)
        {
            return ManifestOperationResult<PackManifest>.Failure(
                $"Unable to lock {FileName} for writing: {exception.Message}"
            );
        }
    }

    private static string FormatIssues(IReadOnlyList<string> issues) =>
        $"Invalid {FileName}:{Environment.NewLine}{string.Join(Environment.NewLine, issues.Select(issue => $"  {GetIssuePath(issue)}: {issue}"))}";

    private static string GetIssuePath(string issue)
    {
        if (GetMetadataIssuePath(issue) is { } metadataPath)
        {
            return metadataPath;
        }

        var isParameterIssue =
            issue.StartsWith("Parameter ", StringComparison.Ordinal)
            || issue.StartsWith("Enum parameter ", StringComparison.Ordinal);
        if (isParameterIssue)
        {
            return "$.parameters";
        }

        if (issue.StartsWith("Managed file ", StringComparison.Ordinal))
        {
            return "$.managedFiles";
        }

        var isHookIssue =
            issue.StartsWith("Lifecycle hook ", StringComparison.Ordinal)
            || issue.StartsWith("Script hook ", StringComparison.Ordinal)
            || issue.StartsWith("Instruction hook ", StringComparison.Ordinal);
        if (isHookIssue)
        {
            return "$.hooks";
        }

        if (issue.StartsWith("Pack reference ", StringComparison.Ordinal))
        {
            return "$.packs";
        }

        var isTagIssue =
            issue.StartsWith("Pack tag", StringComparison.Ordinal)
            || issue.StartsWith("Pack cannot define more than", StringComparison.Ordinal);
        if (isTagIssue)
        {
            return "$.tags";
        }

        return "$";
    }

    private static string? GetMetadataIssuePath(string issue)
    {
        var prefixes = new (string Prefix, string Path)[]
        {
            ("Pack id ", "$.id"),
            ("Version ", "$.version"),
            ("Pack name ", "$.name"),
            ("Pack author ", "$.author"),
            ("Pack homepage ", "$.homepage"),
            ("Pack license ", "$.license"),
        };
        return prefixes
            .FirstOrDefault(value => issue.StartsWith(value.Prefix, StringComparison.Ordinal))
            .Path;
    }
}
