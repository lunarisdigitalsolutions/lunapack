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
    private readonly UserSidecarFile _sidecarFile;

    public UserSettingsStore(IFileSystem fileSystem)
        : this(fileSystem, GetUserProfileDirectory()) { }

    public UserSettingsStore(IFileSystem fileSystem, string userProfileDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userProfileDirectory);
        _fileSystem = fileSystem;
        var profileDirectory = fileSystem.Path.GetFullPath(userProfileDirectory);
        _sidecarFile = new UserSidecarFile(
            fileSystem,
            fileSystem.Path.Combine(profileDirectory, DirectoryName),
            FileName,
            "User settings"
        );
    }

    public string SettingsPath => _sidecarFile.SettingsPath;

    public string SettingsDirectory => _sidecarFile.SettingsDirectory;

    public ManifestOperationResult<string> GetProjectKey(string projectDirectory) =>
        CanonicalProjectPath.Resolve(_fileSystem, projectDirectory);

    public async Task<ManifestOperationResult<UserSettings>> LoadAsync()
    {
        try
        {
            var pathError = _sidecarFile.ValidateExistingPaths();
            if (pathError is not null)
            {
                return ManifestOperationResult<UserSettings>.Failure(pathError);
            }

            if (!_sidecarFile.Exists())
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
                return ManifestOperationResult<UserSettings>.Failure(
                    read.Error ?? "Unable to read user settings.",
                    read.ErrorKind
                );
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
            if (settings is null || !IsValidForLoad(settings))
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
        if (!IsValidForSave(settings))
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
        try
        {
            var pathError = _sidecarFile.ValidateExistingPaths();
            if (pathError is not null)
            {
                return Result(ManifestOperationResult<bool>.Failure(pathError));
            }

            _sidecarFile.WriteAllText(_serializer.Serialize(Normalize(settings)));
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
    }

    private bool IsValidForLoad(UserSettings settings)
    {
        if (settings.Projects is null)
        {
            return false;
        }

        if (!ProjectIdentityMap.ValidateForLoad(_fileSystem, settings.Projects.Keys))
        {
            return false;
        }

        var normalizedSettings = settings with
        {
            Projects = ProjectIdentityMap.NormalizeSeparators(settings.Projects),
        };
        return ManifestModelValidator.Validate(normalizedSettings).Count == 0;
    }

    private bool IsValidForSave(UserSettings settings) =>
        settings.Projects is not null
        && ManifestModelValidator.Validate(settings).Count == 0
        && ProjectIdentityMap.ValidateForSave(_fileSystem, settings.Projects.Keys);

    private UserSettings Normalize(UserSettings settings) =>
        settings with
        {
            Projects = ProjectIdentityMap.Normalize(_fileSystem, settings.Projects),
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
