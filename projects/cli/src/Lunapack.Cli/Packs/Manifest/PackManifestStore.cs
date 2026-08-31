using System.IO.Abstractions;
using Lunapack.Cli.Application.CommandExecution;
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
    ) => WithWriteLockAsync(projectDirectory, () => CreateCoreAsync(projectDirectory, manifest));

    private Task<ManifestOperationResult<PackManifest>> CreateCoreAsync(
        string projectDirectory,
        PackManifest manifest
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
            initialization: true
        );
    }

    public Task<ManifestOperationResult<PackManifest>> LoadAsync(string projectDirectory)
    {
        var manifestPath = GetManifestPath(projectDirectory);
        if (!fileSystem.File.Exists(manifestPath))
        {
            return Task.FromResult(
                ManifestOperationResult<PackManifest>.Failure(
                    $"Missing {FileName} in '{projectDirectory}'."
                )
            );
        }

        try
        {
            var contents = fileSystem.File.ReadAllText(manifestPath);
            var manifest = _deserializer.Deserialize<PackManifest>(contents);
            if (manifest is null)
            {
                return Task.FromResult(
                    ManifestOperationResult<PackManifest>.Failure(
                        $"Invalid {FileName}: manifest is empty."
                    )
                );
            }

            var issues = ManifestModelValidator.Validate(manifest);
            return Task.FromResult(
                issues.Count == 0
                    ? ManifestOperationResult<PackManifest>.Success(
                        PackManifestPathNormalizer.Normalize(manifest)
                    )
                    : ManifestOperationResult<PackManifest>.Failure(FormatIssues(issues))
            );
        }
        catch (Exception exception)
            when (exception
                    is IOException
                        or UnauthorizedAccessException
                        or YamlDotNet.Core.YamlException
            )
        {
            return Task.FromResult(
                ManifestOperationResult<PackManifest>.Failure(
                    $"Unable to read {FileName}: {exception.Message}"
                )
            );
        }
    }

    public async Task<ManifestOperationResult<PackManifest>> UpdateAsync(
        string projectDirectory,
        Func<PackManifest, string?> mutation
    ) =>
        await WithWriteLockAsync(
            projectDirectory,
            () => UpdateCoreAsync(projectDirectory, mutation)
        );

    private async Task<ManifestOperationResult<PackManifest>> UpdateCoreAsync(
        string projectDirectory,
        Func<PackManifest, string?> mutation
    )
    {
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
            originalContents = fileSystem.File.ReadAllText(manifestPath);
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
            originalContents
        );
    }

    private Task<ManifestOperationResult<PackManifest>> WriteAsync(
        string projectDirectory,
        PackManifest manifest,
        string? expectedContents,
        bool initialization = false
    )
    {
        var issues = ManifestModelValidator.Validate(manifest);
        if (issues.Count > 0)
        {
            return Task.FromResult(
                ManifestOperationResult<PackManifest>.Failure(FormatIssues(issues))
            );
        }

        var manifestPath = GetManifestPath(projectDirectory);
        var temporaryPath = $"{manifestPath}.{Guid.NewGuid():N}.tmp";
        try
        {
            fileSystem.Directory.CreateDirectory(projectDirectory);
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
            fileSystem.File.WriteAllText(temporaryPath, contents);
            var manifestChanged =
                expectedContents is not null
                && (
                    !fileSystem.File.Exists(manifestPath)
                    || !string.Equals(
                        fileSystem.File.ReadAllText(manifestPath),
                        expectedContents,
                        StringComparison.Ordinal
                    )
                );
            if (manifestChanged)
            {
                return Task.FromResult(
                    ManifestOperationResult<PackManifest>.Failure(
                        $"{FileName} changed while the command was running."
                    )
                );
            }

            fileSystem.File.Move(
                temporaryPath,
                manifestPath,
                overwrite: expectedContents is not null
            );
            return Task.FromResult(ManifestOperationResult<PackManifest>.Success(manifest));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return Task.FromResult(
                ManifestOperationResult<PackManifest>.Failure(
                    $"Unable to write {FileName}: {exception.Message}"
                )
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
