using System.IO.Abstractions;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Lunapack.Cli;

internal sealed class PackManifestStore(IFileSystem fileSystem)
{
    public const string FileName = "pack.yml";

    private static readonly IDeserializer _deserializer = new StaticDeserializerBuilder(
        new LunapackYamlContext()
    )
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .Build();

    private static readonly ISerializer _serializer = new StaticSerializerBuilder(
        new LunapackYamlContext()
    )
        .ConfigureDefaultValuesHandling(DefaultValuesHandling.OmitNull)
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .Build();

    public Task<ManifestOperationResult<PackManifest>> CreateAsync(
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

        return WriteAsync(projectDirectory, PackManifestPathNormalizer.Normalize(manifest), null);
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
        string? expectedContents
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
            fileSystem.File.WriteAllText(temporaryPath, _serializer.Serialize(manifest));
            if (
                expectedContents is not null
                && (
                    !fileSystem.File.Exists(manifestPath)
                    || !string.Equals(
                        fileSystem.File.ReadAllText(manifestPath),
                        expectedContents,
                        StringComparison.Ordinal
                    )
                )
            )
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

    private static string FormatIssues(IReadOnlyList<string> issues) =>
        $"Invalid {FileName}:{Environment.NewLine}{string.Join(Environment.NewLine, issues.Select(issue => $"  {issue}"))}";
}
