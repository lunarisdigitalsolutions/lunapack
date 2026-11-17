using System.IO.Abstractions;
using Lunapack.Cli.Application.CommandExecution;
using Lunapack.Cli.Application.Paths;
using Lunapack.Cli.Application.Serialization;
using Lunapack.Cli.Packs.Manifest;
using Lunapack.Cli.Trust;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Lunapack.Cli.Sources;

internal sealed class SourceIgnoreStore
{
    public const string FileName = "source-ignores.yml";

    private static readonly IDeserializer _deserializer = new StaticDeserializerBuilder(
        new LunapackYamlContext()
    )
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .Build();

    private static readonly ISerializer _serializer = new StaticSerializerBuilder(
        new LunapackYamlContext()
    )
        .ConfigureDefaultValuesHandling(DefaultValuesHandling.OmitNull)
        .DisableAliases()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .Build();

    private readonly IFileSystem _fileSystem;
    private readonly string _settingsDirectory;

    public SourceIgnoreStore(IFileSystem fileSystem, string settingsDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(settingsDirectory);
        _fileSystem = fileSystem;
        _settingsDirectory = fileSystem.Path.GetFullPath(settingsDirectory);
        SettingsPath = fileSystem.Path.Combine(_settingsDirectory, FileName);
    }

    public string SettingsPath { get; }

    public Task<ManifestOperationResult<SourceIgnoreSettings>> LoadAsync()
    {
        try
        {
            var pathError = ValidateExistingPaths();
            if (pathError is not null)
            {
                return Result(ManifestOperationResult<SourceIgnoreSettings>.Failure(pathError));
            }

            if (!_fileSystem.File.Exists(SettingsPath))
            {
                return Result(
                    ManifestOperationResult<SourceIgnoreSettings>.Success(
                        new SourceIgnoreSettings()
                    )
                );
            }

            var settings = _deserializer.Deserialize<SourceIgnoreSettings>(
                _fileSystem.File.ReadAllText(SettingsPath)
            );
            if (settings is null || !IsValid(settings))
            {
                return Result(
                    ManifestOperationResult<SourceIgnoreSettings>.Failure(
                        $"Invalid source-ignore settings in '{SettingsPath}'."
                    )
                );
            }

            return Result(
                ManifestOperationResult<SourceIgnoreSettings>.Success(Normalize(settings))
            );
        }
        catch (Exception exception)
            when (exception
                    is IOException
                        or UnauthorizedAccessException
                        or YamlDotNet.Core.YamlException
            )
        {
            return Result(
                ManifestOperationResult<SourceIgnoreSettings>.Failure(
                    $"Unable to read source-ignore settings: {exception.Message}"
                )
            );
        }
    }

    public Task<ManifestOperationResult<bool>> SaveAsync(SourceIgnoreSettings settings)
    {
        if (!IsValid(settings))
        {
            return Result(
                ManifestOperationResult<bool>.Failure(
                    "Refusing to write invalid source-ignore settings."
                )
            );
        }

        var temporaryPath = _fileSystem.Path.Combine(
            _settingsDirectory,
            $".{FileName}.{Guid.NewGuid():N}.tmp"
        );
        try
        {
            var pathError = ValidateExistingPaths();
            if (pathError is not null)
            {
                return Result(ManifestOperationResult<bool>.Failure(pathError));
            }

            EnsureSettingsDirectory();
            _fileSystem.File.WriteAllText(
                temporaryPath,
                _serializer.Serialize(Normalize(settings))
            );
            UserSettingsPathSecurity.Apply(temporaryPath, directory: false);
            _fileSystem.File.Move(temporaryPath, SettingsPath, overwrite: true);
            return Result(ManifestOperationResult<bool>.Success(true));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return Result(
                ManifestOperationResult<bool>.Failure(
                    $"Unable to write source-ignore settings: {exception.Message}"
                )
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

    private string? ValidateExistingPaths()
    {
        if (_fileSystem.File.Exists(_settingsDirectory))
        {
            return $"Source-ignore settings path '{_settingsDirectory}' must be a directory.";
        }

        if (_fileSystem.Directory.Exists(_settingsDirectory))
        {
            var directoryError = UserSettingsPathSecurity.ValidateExisting(
                _fileSystem,
                _settingsDirectory,
                directory: true
            );
            if (directoryError is not null)
            {
                return directoryError;
            }
        }

        if (_fileSystem.Directory.Exists(SettingsPath))
        {
            return $"Source-ignore settings path '{SettingsPath}' must be a regular file.";
        }

        return _fileSystem.File.Exists(SettingsPath)
            ? UserSettingsPathSecurity.ValidateExisting(_fileSystem, SettingsPath, directory: false)
            : null;
    }

    private void EnsureSettingsDirectory()
    {
        if (!_fileSystem.Directory.Exists(_settingsDirectory))
        {
            _fileSystem.Directory.CreateDirectory(_settingsDirectory);
            UserSettingsPathSecurity.Apply(_settingsDirectory, directory: true);
        }
    }

    private bool IsValid(SourceIgnoreSettings settings)
    {
        if (ManifestModelValidator.Validate(settings).Count != 0)
        {
            return false;
        }

        foreach (var projectPath in settings.Projects.Keys)
        {
            var fullPath = _fileSystem.Path.GetFullPath(projectPath);
            var normalizedPath = ProjectPath.Normalize(
                _fileSystem.Path.TrimEndingDirectorySeparator(fullPath)
            );
            if (!string.Equals(projectPath, normalizedPath, StringComparison.Ordinal))
            {
                return false;
            }

            if (_fileSystem.Directory.Exists(fullPath))
            {
                var canonicalPath = CanonicalProjectPath.Resolve(_fileSystem, fullPath);
                var hasExpectedCanonicalPath =
                    canonicalPath.Value is { } value
                    && string.Equals(projectPath, value, StringComparison.Ordinal);
                if (!hasExpectedCanonicalPath)
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static SourceIgnoreSettings Normalize(SourceIgnoreSettings settings) =>
        settings with
        {
            Projects = new Dictionary<string, List<ConfiguredSourceIdentity>>(
                settings.Projects,
                StringComparer.Ordinal
            ),
        };

    private static Task<ManifestOperationResult<T>> Result<T>(ManifestOperationResult<T> result) =>
        Task.FromResult(result);
}
