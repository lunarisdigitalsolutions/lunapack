using System.IO.Abstractions;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Lunapack.Cli;

internal sealed class UserSettingsStore
{
    public const string DirectoryName = ".lunapack";
    public const string FileName = "config.yml";

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
    private readonly string _settingsPath;

    public UserSettingsStore(IFileSystem fileSystem)
        : this(fileSystem, GetUserProfileDirectory()) { }

    public UserSettingsStore(IFileSystem fileSystem, string userProfileDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userProfileDirectory);
        _fileSystem = fileSystem;
        var profileDirectory = fileSystem.Path.GetFullPath(userProfileDirectory);
        _settingsDirectory = fileSystem.Path.Combine(profileDirectory, DirectoryName);
        _settingsPath = fileSystem.Path.Combine(_settingsDirectory, FileName);
    }

    public string SettingsPath => _settingsPath;

    public ManifestOperationResult<string> GetProjectKey(string projectDirectory) =>
        CanonicalProjectPath.Resolve(_fileSystem, projectDirectory);

    public Task<ManifestOperationResult<UserSettings>> LoadAsync()
    {
        try
        {
            var pathError = ValidateExistingPaths();
            if (pathError is not null)
            {
                return Result(ManifestOperationResult<UserSettings>.Failure(pathError));
            }

            if (!_fileSystem.File.Exists(_settingsPath))
            {
                return Result(ManifestOperationResult<UserSettings>.Success(new UserSettings()));
            }

            var settings = _deserializer.Deserialize<UserSettings>(
                _fileSystem.File.ReadAllText(_settingsPath)
            );
            if (settings is null || !IsValid(settings))
            {
                return Result(
                    ManifestOperationResult<UserSettings>.Failure(
                        $"Invalid user settings in '{_settingsPath}'."
                    )
                );
            }

            return Result(ManifestOperationResult<UserSettings>.Success(Normalize(settings)));
        }
        catch (Exception exception)
            when (exception
                    is IOException
                        or UnauthorizedAccessException
                        or YamlDotNet.Core.YamlException
            )
        {
            return Result(
                ManifestOperationResult<UserSettings>.Failure(
                    $"Unable to read user settings: {exception.Message}"
                )
            );
        }
    }

    public Task<ManifestOperationResult<bool>> SaveAsync(UserSettings settings)
    {
        if (!IsValid(settings))
        {
            return Result(
                ManifestOperationResult<bool>.Failure("Refusing to write invalid user settings.")
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
            _fileSystem.File.Move(temporaryPath, _settingsPath, overwrite: true);
            return Result(ManifestOperationResult<bool>.Success(true));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return Result(
                ManifestOperationResult<bool>.Failure(
                    $"Unable to write user settings: {exception.Message}"
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
            return $"User settings path '{_settingsDirectory}' must be a directory.";
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

        if (_fileSystem.Directory.Exists(_settingsPath))
        {
            return $"User settings path '{_settingsPath}' must be a regular file.";
        }

        return _fileSystem.File.Exists(_settingsPath)
            ? UserSettingsPathSecurity.ValidateExisting(
                _fileSystem,
                _settingsPath,
                directory: false
            )
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

    private bool IsValid(UserSettings settings)
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
                var canonicalPath = GetProjectKey(fullPath);
                if (
                    canonicalPath.Value is not { } value
                    || !string.Equals(projectPath, value, StringComparison.Ordinal)
                )
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static UserSettings Normalize(UserSettings settings) =>
        settings with
        {
            Projects = new Dictionary<string, LocalProjectTrust>(
                settings.Projects,
                StringComparer.Ordinal
            ),
        };

    private static Task<ManifestOperationResult<T>> Result<T>(ManifestOperationResult<T> result) =>
        Task.FromResult(result);

    private static string GetUserProfileDirectory()
    {
        var path = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return string.IsNullOrEmpty(path)
            ? throw new InvalidOperationException("Unable to locate the current user profile.")
            : path;
    }
}
