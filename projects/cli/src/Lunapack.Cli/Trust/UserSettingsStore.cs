using System.IO.Abstractions;
using System.Text;
using Lunapack.Cli.Application.CommandExecution;
using Lunapack.Cli.Application.Paths;
using Lunapack.Cli.Application.Preferences;
using Lunapack.Cli.Application.Serialization;
using Lunapack.Cli.Packs.Manifest;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Lunapack.Cli.Trust;

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

    public UserSettingsStore(IFileSystem fileSystem)
        : this(fileSystem, GetUserProfileDirectory()) { }

    public UserSettingsStore(IFileSystem fileSystem, string userProfileDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userProfileDirectory);
        _fileSystem = fileSystem;
        var profileDirectory = fileSystem.Path.GetFullPath(userProfileDirectory);
        SettingsDirectory = fileSystem.Path.Combine(profileDirectory, DirectoryName);
        SettingsPath = fileSystem.Path.Combine(SettingsDirectory, FileName);
    }

    public string SettingsPath { get; }

    public string SettingsDirectory { get; }

    public ManifestOperationResult<string> GetProjectKey(string projectDirectory) =>
        CanonicalProjectPath.Resolve(_fileSystem, projectDirectory);

    public async Task<ManifestOperationResult<UserSettings>> LoadAsync()
    {
        try
        {
            var pathError = ValidateExistingPaths();
            if (pathError is not null)
            {
                return ManifestOperationResult<UserSettings>.Failure(pathError);
            }

            if (!_fileSystem.File.Exists(SettingsPath))
            {
                return ManifestOperationResult<UserSettings>.Success(new UserSettings());
            }

            var read = await BoundedFileReader.ReadTextAsync(
                _fileSystem,
                SettingsPath,
                ResourceLimits.UserSettingsBootstrapBytes,
                "User settings"
            );
            if (read.Value is not { } contents)
            {
                return ManifestOperationResult<UserSettings>.Failure(read.Error!);
            }

            var unknownResourceLimit = ResourceLimitYamlValidator.ValidateUserSettings(
                contents.Text
            );
            if (unknownResourceLimit is not null)
            {
                return ManifestOperationResult<UserSettings>.Failure(
                    $"Invalid user settings in '{SettingsPath}': {unknownResourceLimit}"
                );
            }

            var settings = _deserializer.Deserialize<UserSettings>(contents.Text);
            if (settings is null || !IsValid(settings))
            {
                return ManifestOperationResult<UserSettings>.Failure(
                    $"Invalid user settings in '{SettingsPath}'."
                );
            }

            return ManifestOperationResult<UserSettings>.Success(Normalize(settings));
        }
        catch (Exception exception)
            when (exception
                    is IOException
                        or UnauthorizedAccessException
                        or YamlDotNet.Core.YamlException
            )
        {
            return ManifestOperationResult<UserSettings>.Failure(
                $"Unable to read user settings: {exception.Message}"
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

        var serialized = _serializer.Serialize(Normalize(settings));
        var serializedByteCount = Encoding.UTF8.GetByteCount(serialized);
        if (serializedByteCount > ResourceLimits.UserSettingsBootstrapBytes)
        {
            return Result(
                ManifestOperationResult<bool>.Failure(
                    $"User settings would exceed the {ResourceLimits.UserSettingsBootstrapBytes} byte bootstrap limit ({serializedByteCount} bytes)."
                )
            );
        }

        var temporaryPath = _fileSystem.Path.Combine(
            SettingsDirectory,
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
            _fileSystem.File.WriteAllText(temporaryPath, serialized);
            UserSettingsPathSecurity.Apply(temporaryPath, directory: false);
            _fileSystem.File.Move(temporaryPath, SettingsPath, overwrite: true);
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
        if (_fileSystem.File.Exists(SettingsDirectory))
        {
            return $"User settings path '{SettingsDirectory}' must be a directory.";
        }

        if (_fileSystem.Directory.Exists(SettingsDirectory))
        {
            var directoryError = UserSettingsPathSecurity.ValidateExisting(
                _fileSystem,
                SettingsDirectory,
                directory: true
            );
            if (directoryError is not null)
            {
                return directoryError;
            }
        }

        if (_fileSystem.Directory.Exists(SettingsPath))
        {
            return $"User settings path '{SettingsPath}' must be a regular file.";
        }

        return _fileSystem.File.Exists(SettingsPath)
            ? UserSettingsPathSecurity.ValidateExisting(_fileSystem, SettingsPath, directory: false)
            : null;
    }

    private void EnsureSettingsDirectory()
    {
        if (!_fileSystem.Directory.Exists(SettingsDirectory))
        {
            _fileSystem.Directory.CreateDirectory(SettingsDirectory);
            UserSettingsPathSecurity.Apply(SettingsDirectory, directory: true);
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
        var configuredPath = Environment.GetEnvironmentVariable("LUNAPACK_USER_PROFILE");
        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            return configuredPath;
        }

        var path = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return string.IsNullOrEmpty(path)
            ? throw new InvalidOperationException("Unable to locate the current user profile.")
            : path;
    }
}
