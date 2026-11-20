using System.IO.Abstractions;
using Lunapack.Cli.Application.CommandExecution;
using Lunapack.Cli.Application.Paths;
using Lunapack.Cli.Application.Serialization;
using Lunapack.Cli.Packs.Manifest;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Lunapack.Cli.Trust;

internal sealed class LifecycleHookPolicyStore
{
    public const string FileName = "lifecycle-hook-policy.yml";

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

    public LifecycleHookPolicyStore(IFileSystem fileSystem, string settingsDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(settingsDirectory);
        _fileSystem = fileSystem;
        _sidecarFile = new UserSidecarFile(
            fileSystem,
            settingsDirectory,
            FileName,
            "Lifecycle-hook policy settings"
        );
    }

    public string SettingsPath => _sidecarFile.SettingsPath;

    public Task<ManifestOperationResult<LifecycleHookPolicySettings>> LoadAsync()
    {
        try
        {
            var pathError = _sidecarFile.ValidateExistingPaths();
            if (pathError is not null)
            {
                return Task.FromResult(
                    ManifestOperationResult<LifecycleHookPolicySettings>.Failure(pathError)
                );
            }

            if (!_sidecarFile.Exists())
            {
                return Task.FromResult(
                    ManifestOperationResult<LifecycleHookPolicySettings>.Success(
                        new LifecycleHookPolicySettings()
                    )
                );
            }

            var settings = _deserializer.Deserialize<LifecycleHookPolicySettings>(
                _sidecarFile.ReadAllText()
            );
            if (settings is null || !IsValidForLoad(settings))
            {
                return Task.FromResult(
                    ManifestOperationResult<LifecycleHookPolicySettings>.Failure(
                        $"Invalid lifecycle-hook policy settings in '{SettingsPath}'."
                    )
                );
            }

            return Task.FromResult(
                ManifestOperationResult<LifecycleHookPolicySettings>.Success(Normalize(settings))
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
                ManifestOperationResult<LifecycleHookPolicySettings>.Failure(
                    $"Unable to read lifecycle-hook policy settings: {exception.Message}"
                )
            );
        }
    }

    public Task<ManifestOperationResult<bool>> SaveAsync(LifecycleHookPolicySettings settings)
    {
        if (!IsValidForSave(settings))
        {
            return Task.FromResult(
                ManifestOperationResult<bool>.Failure(
                    "Refusing to write invalid lifecycle-hook policy settings."
                )
            );
        }

        try
        {
            var pathError = _sidecarFile.ValidateExistingPaths();
            if (pathError is not null)
            {
                return Task.FromResult(ManifestOperationResult<bool>.Failure(pathError));
            }

            _sidecarFile.WriteAllText(_serializer.Serialize(Normalize(settings)));
            return Task.FromResult(ManifestOperationResult<bool>.Success(true));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return Task.FromResult(
                ManifestOperationResult<bool>.Failure(
                    $"Unable to write lifecycle-hook policy settings: {exception.Message}"
                )
            );
        }
    }

    private bool IsValidForLoad(LifecycleHookPolicySettings settings)
    {
        if (settings.Global is null || settings.Projects is null)
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

    private bool IsValidForSave(LifecycleHookPolicySettings settings) =>
        settings.Global is not null
        && settings.Projects is not null
        && ManifestModelValidator.Validate(settings).Count == 0
        && ProjectIdentityMap.ValidateForSave(_fileSystem, settings.Projects.Keys);

    private LifecycleHookPolicySettings Normalize(LifecycleHookPolicySettings settings) =>
        settings with
        {
            Projects = ProjectIdentityMap.Normalize(_fileSystem, settings.Projects),
        };
}
