using System.IO.Abstractions;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Lunapack.Cli;

internal sealed class ProjectManifestStore
{
    public const string FileName = "lunapack.yml";

    private static readonly IDeserializer _deserializer = new StaticDeserializerBuilder(
        new LunapackYamlContext()
    )
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .Build();

    private readonly IFileSystem _fileSystem;

    private static readonly ISerializer _serializer = new StaticSerializerBuilder(
        new LunapackYamlContext()
    )
        .ConfigureDefaultValuesHandling(DefaultValuesHandling.OmitNull)
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .Build();

    public ProjectManifestStore(IFileSystem fileSystem)
    {
        this._fileSystem = fileSystem;
    }

    public async Task<ManifestOperationResult<ProjectManifest>> LoadAsync(string projectDirectory)
    {
        var manifestPath = GetManifestPath(projectDirectory);
        if (!_fileSystem.File.Exists(manifestPath))
        {
            return ManifestOperationResult<ProjectManifest>.Failure(
                $"Missing {FileName} in '{projectDirectory}'."
            );
        }

        try
        {
            var manifest = _deserializer.Deserialize<ProjectManifest>(
                _fileSystem.File.ReadAllText(manifestPath)
            );
            if (manifest is null || !await IsValidAsync(manifest))
            {
                return ManifestOperationResult<ProjectManifest>.Failure(
                    $"Invalid {FileName} in '{projectDirectory}'."
                );
            }

            return ManifestOperationResult<ProjectManifest>.Success(manifest);
        }
        catch (Exception exception)
            when (exception
                    is IOException
                        or UnauthorizedAccessException
                        or YamlDotNet.Core.YamlException
            )
        {
            return ManifestOperationResult<ProjectManifest>.Failure(
                $"Unable to read {FileName} in '{projectDirectory}': {exception.Message}"
            );
        }
    }

    public async Task<ManifestOperationResult<bool>> SaveAsync(
        string projectDirectory,
        ProjectManifest manifest
    )
    {
        if (!await IsValidAsync(manifest))
        {
            return ManifestOperationResult<bool>.Failure(
                "Refusing to write a manifest that does not match the schema."
            );
        }

        var manifestPath = GetManifestPath(projectDirectory);
        var temporaryPath = $"{manifestPath}.{Guid.NewGuid():N}.tmp";

        try
        {
            _fileSystem.File.WriteAllText(temporaryPath, _serializer.Serialize(manifest));
            _fileSystem.File.Move(temporaryPath, manifestPath, overwrite: true);

            return ManifestOperationResult<bool>.Success(true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return ManifestOperationResult<bool>.Failure(
                $"Unable to write {FileName}: {exception.Message}"
            );
        }
        finally
        {
            if (_fileSystem.File.Exists(temporaryPath))
            {
                _fileSystem.File.Delete(temporaryPath);
            }
        }
    }

    private string GetManifestPath(string projectDirectory) =>
        _fileSystem.Path.Combine(projectDirectory, FileName);

    private static async Task<bool> IsValidAsync(ProjectManifest manifest)
    {
        return await Task.FromResult(ManifestModelValidator.Validate(manifest).Count == 0);
    }
}
