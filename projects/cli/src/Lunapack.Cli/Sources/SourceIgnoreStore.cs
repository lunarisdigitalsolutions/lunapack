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
    private readonly UserSidecarFile _sidecarFile;

    public SourceIgnoreStore(IFileSystem fileSystem, string settingsDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(settingsDirectory);
        _fileSystem = fileSystem;
        _sidecarFile = new UserSidecarFile(
            fileSystem,
            settingsDirectory,
            FileName,
            "Source-ignore settings"
        );
    }

    public string SettingsPath => _sidecarFile.SettingsPath;

    public Task<ManifestOperationResult<SourceIgnoreSettings>> LoadAsync()
    {
        try
        {
            var pathError = _sidecarFile.ValidateExistingPaths();
            if (pathError is not null)
            {
                return Result(ManifestOperationResult<SourceIgnoreSettings>.Failure(pathError));
            }

            if (!_sidecarFile.Exists())
            {
                return Result(
                    ManifestOperationResult<SourceIgnoreSettings>.Success(
                        new SourceIgnoreSettings()
                    )
                );
            }

            var settings = _deserializer.Deserialize<SourceIgnoreSettings>(
                _sidecarFile.ReadAllText()
            );
            if (settings is null || !IsValidForLoad(settings))
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
        if (!IsValidForSave(settings))
        {
            return Result(
                ManifestOperationResult<bool>.Failure(
                    "Refusing to write invalid source-ignore settings."
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
                    $"Unable to write source-ignore settings: {exception.Message}"
                )
            );
        }
    }

    private bool IsValidForLoad(SourceIgnoreSettings settings)
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

    private bool IsValidForSave(SourceIgnoreSettings settings) =>
        settings.Projects is not null
        && ManifestModelValidator.Validate(settings).Count == 0
        && ProjectIdentityMap.ValidateForSave(_fileSystem, settings.Projects.Keys);

    private SourceIgnoreSettings Normalize(SourceIgnoreSettings settings) =>
        settings with
        {
            Projects = ProjectIdentityMap.Normalize(_fileSystem, settings.Projects),
        };

    private static Task<ManifestOperationResult<T>> Result<T>(ManifestOperationResult<T> result) =>
        Task.FromResult(result);
}
