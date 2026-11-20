using System.IO.Abstractions;
using System.Text;
using Lunapack.Cli.Application.CommandExecution;
using Lunapack.Cli.Application.Paths;
using Lunapack.Cli.Application.Preferences;
using Lunapack.Cli.Application.Serialization;
using Lunapack.Cli.Packs.Manifest;
using Lunapack.Cli.Sources;
using Lunapack.Cli.Trust;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Lunapack.Cli.Project;

internal sealed class ProjectStateStore(
    IFileSystem fileSystem,
    UserSettingsStore? userSettingsStore = null
) : IProjectStateStore
{
    public const string ConfigurationFileName = "lunapack.yml";

    public const string LockFileName = "lunapack-lock.yml";

    private static readonly IDeserializer _deserializer = new StaticDeserializerBuilder(
        new LunapackYamlContext()
    )
        .IgnoreUnmatchedProperties()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .WithTypeConverter(new ProjectConfigurationSourceYamlTypeConverter())
        .WithTypeConverter(new ScalarValueDictionaryYamlTypeConverter())
        .Build();

    private static readonly ISerializer _serializer = new StaticSerializerBuilder(
        new LunapackYamlContext()
    )
        .ConfigureDefaultValuesHandling(DefaultValuesHandling.OmitNull)
        .DisableAliases()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .WithTypeConverter(new ScalarValueDictionaryYamlTypeConverter())
        .Build();

    private readonly IFileSystem _fileSystem = fileSystem;

    public async Task<ManifestOperationResult<ProjectState>> LoadAsync(string projectDirectory)
    {
        var userPreferences = await LoadUserResourceLimitPreferencesAsync(projectDirectory);
        if (userPreferences.Value is not { } userResourcePreferences)
        {
            return ManifestOperationResult<ProjectState>.Failure(userPreferences.Error!);
        }

        var userLimits = ResourceLimits.Resolve(
            local: null,
            userResourcePreferences.UserProject,
            userResourcePreferences.Global
        );
        var configuration = await LoadDocumentAsync<ProjectConfiguration>(
            projectDirectory,
            ConfigurationFileName,
            ManifestModelValidator.Validate,
            Math.Min(userLimits.ProjectManifestBytes, ResourceLimits.ProjectSettingsBootstrapBytes)
        );
        if (configuration.Value is not { } configurationDocument)
        {
            return ManifestOperationResult<ProjectState>.Failure(
                configuration.Error ?? "Unable to load project configuration."
            );
        }

        var loadedConfiguration = configurationDocument.Document;
        var resourceLimits = ResourceLimits.Resolve(
            loadedConfiguration.Preferences?.ResourceLimits,
            userResourcePreferences.UserProject,
            userResourcePreferences.Global
        );
        if (configurationDocument.ByteCount > resourceLimits.ProjectManifestBytes)
        {
            return ManifestOperationResult<ProjectState>.Failure(
                $"{ConfigurationFileName} exceeds its effective limit ({configurationDocument.ByteCount} bytes read, limit {resourceLimits.ProjectManifestBytes} bytes)."
            );
        }

        var lockFile = await LoadDocumentAsync<ProjectLockFile>(
            projectDirectory,
            LockFileName,
            ManifestModelValidator.Validate,
            resourceLimits.LockFileBytes,
            contents => LockFileYamlResourceValidator.Validate(contents, resourceLimits)
        );
        if (lockFile.Value is not { } lockFileDocument)
        {
            return ManifestOperationResult<ProjectState>.Failure(
                lockFile.Error ?? "Unable to load project lock file.",
                lockFile.ErrorKind
            );
        }

        var loadedLockFile = lockFileDocument.Document;
        var normalizedState = NormalizeState(
            new ProjectState
            {
                Configuration = loadedConfiguration,
                LockFile = loadedLockFile,
                EffectiveResourceLimits = resourceLimits,
            }
        );
        var validation = ValidateState(
            normalizedState.Configuration,
            normalizedState.LockFile,
            normalizedState.EffectiveResourceLimits,
            allowUnconfiguredLockSources: true
        );
        if (!validation.IsSuccess)
        {
            return ManifestOperationResult<ProjectState>.Failure(
                validation.Error!,
                validation.ErrorKind
            );
        }

        return ManifestOperationResult<ProjectState>.Success(normalizedState);
    }

    public async Task<ManifestOperationResult<bool>> InitializeAsync(string projectDirectory)
    {
        var configuration = new ProjectConfiguration { SchemaVersion = 1 };
        var lockFile = new ProjectLockFile { SchemaVersion = 1 };
        var hasInvalidInitialState =
            !await IsValidAsync(configuration, ManifestModelValidator.Validate)
            || !await IsValidAsync(lockFile, ManifestModelValidator.Validate);
        if (hasInvalidInitialState)
        {
            return ManifestOperationResult<bool>.Failure(
                "Refusing to initialize project state that does not match the schemas."
            );
        }

        var configurationPath = GetDocumentPath(projectDirectory, ConfigurationFileName);
        var lockFilePath = GetDocumentPath(projectDirectory, LockFileName);
        if (_fileSystem.File.Exists(configurationPath) || _fileSystem.File.Exists(lockFilePath))
        {
            return ManifestOperationResult<bool>.Failure("Project state already exists.");
        }

        var initialization = await PrepareInitializationAsync(
            projectDirectory,
            configuration,
            lockFile
        );
        if (initialization.Value is not { } serialized)
        {
            return ManifestOperationResult<bool>.Failure(
                initialization.Error!,
                initialization.ErrorKind
            );
        }

        var temporaryConfigurationPath = CreateTemporaryPath(configurationPath);
        var temporaryLockFilePath = CreateTemporaryPath(lockFilePath);
        try
        {
            _fileSystem.File.WriteAllText(temporaryConfigurationPath, serialized.Configuration);
            _fileSystem.File.WriteAllText(temporaryLockFilePath, serialized.LockFile);
            _fileSystem.File.Move(temporaryConfigurationPath, configurationPath);
            _fileSystem.File.Move(temporaryLockFilePath, lockFilePath);
            return ManifestOperationResult<bool>.Success(true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            if (_fileSystem.File.Exists(configurationPath))
            {
                _fileSystem.File.Delete(configurationPath);
            }

            if (_fileSystem.File.Exists(lockFilePath))
            {
                _fileSystem.File.Delete(lockFilePath);
            }

            return ManifestOperationResult<bool>.Failure(
                $"Unable to initialize project state: {exception.Message}"
            );
        }
        finally
        {
            DeleteTemporaryFile(temporaryConfigurationPath);
            DeleteTemporaryFile(temporaryLockFilePath);
        }
    }

    private async Task<ManifestOperationResult<SerializedProjectState>> PrepareInitializationAsync(
        string projectDirectory,
        ProjectConfiguration configuration,
        ProjectLockFile lockFile
    )
    {
        var resourceLimitsResult = await ResolveSaveResourceLimitsAsync(
            projectDirectory,
            new ProjectState { Configuration = configuration, LockFile = lockFile }
        );
        if (resourceLimitsResult.Value is not { } resourceLimits)
        {
            return ManifestOperationResult<SerializedProjectState>.Failure(
                resourceLimitsResult.Error!,
                resourceLimitsResult.ErrorKind
            );
        }

        var serializedConfiguration = SerializeBounded(
            new InitialProjectConfiguration { SchemaVersion = configuration.SchemaVersion },
            resourceLimits.ProjectManifestBytes,
            ConfigurationFileName
        );
        if (serializedConfiguration.Value is not { } configurationContents)
        {
            return ManifestOperationResult<SerializedProjectState>.Failure(
                serializedConfiguration.Error!,
                serializedConfiguration.ErrorKind
            );
        }

        var serializedLockFile = SerializeBounded(
            new InitialProjectLockFile { SchemaVersion = lockFile.SchemaVersion },
            resourceLimits.LockFileBytes,
            LockFileName
        );
        return serializedLockFile.Value is { } lockFileContents
            ? ManifestOperationResult<SerializedProjectState>.Success(
                new SerializedProjectState(configurationContents, lockFileContents)
            )
            : ManifestOperationResult<SerializedProjectState>.Failure(
                serializedLockFile.Error!,
                serializedLockFile.ErrorKind
            );
    }

    public async Task<ManifestOperationResult<bool>> SaveAsync(
        string projectDirectory,
        ProjectState state
    ) => await SaveAsync(projectDirectory, state, allowUnconfiguredLockSources: false);

    public async Task<ManifestOperationResult<bool>> ValidateForSaveAsync(
        string projectDirectory,
        ProjectState state
    )
    {
        var prepared = await PrepareForSaveAsync(
            projectDirectory,
            state,
            allowUnconfiguredLockSources: false
        );
        return prepared.IsSuccess
            ? ManifestOperationResult<bool>.Success(true)
            : ManifestOperationResult<bool>.Failure(prepared.Error!, prepared.ErrorKind);
    }

    public async Task<ManifestOperationResult<bool>> ValidateForSaveAllowingUnavailableSourcesAsync(
        string projectDirectory,
        ProjectState state
    )
    {
        var prepared = await PrepareForSaveAsync(
            projectDirectory,
            state,
            allowUnconfiguredLockSources: true
        );
        return prepared.IsSuccess
            ? ManifestOperationResult<bool>.Success(true)
            : ManifestOperationResult<bool>.Failure(prepared.Error!, prepared.ErrorKind);
    }

    public async Task<ManifestOperationResult<bool>> SaveAllowingUnavailableSourcesAsync(
        string projectDirectory,
        ProjectState state
    ) => await SaveAsync(projectDirectory, state, allowUnconfiguredLockSources: true);

    private async Task<ManifestOperationResult<bool>> SaveAsync(
        string projectDirectory,
        ProjectState state,
        bool allowUnconfiguredLockSources
    )
    {
        var prepared = await PrepareForSaveAsync(
            projectDirectory,
            state,
            allowUnconfiguredLockSources
        );
        if (prepared.Value is not { } preparedState)
        {
            return ManifestOperationResult<bool>.Failure(prepared.Error!, prepared.ErrorKind);
        }

        var configurationPath = GetDocumentPath(projectDirectory, ConfigurationFileName);
        var lockFilePath = GetDocumentPath(projectDirectory, LockFileName);
        var snapshotsResult = await CreateSnapshotsAsync(
            configurationPath,
            lockFilePath,
            preparedState.State.EffectiveResourceLimits
        );
        if (snapshotsResult.Value is not { } snapshots)
        {
            return ManifestOperationResult<bool>.Failure(
                snapshotsResult.Error!,
                snapshotsResult.ErrorKind
            );
        }

        var temporaryConfigurationPath = CreateTemporaryPath(configurationPath);
        var temporaryLockFilePath = CreateTemporaryPath(lockFilePath);

        try
        {
            _fileSystem.File.WriteAllText(
                temporaryConfigurationPath,
                preparedState.Contents.Configuration
            );
            _fileSystem.File.WriteAllText(temporaryLockFilePath, preparedState.Contents.LockFile);

            _fileSystem.File.Move(temporaryConfigurationPath, configurationPath, overwrite: true);
            _fileSystem.File.Move(temporaryLockFilePath, lockFilePath, overwrite: true);

            return ManifestOperationResult<bool>.Success(true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            RestoreSnapshots(projectDirectory, snapshots);
            return ManifestOperationResult<bool>.Failure(
                $"Unable to write project state: {exception.Message}"
            );
        }
        finally
        {
            DeleteTemporaryFile(temporaryConfigurationPath);
            DeleteTemporaryFile(temporaryLockFilePath);
        }
    }

    private async Task<ManifestOperationResult<PreparedProjectState>> PrepareForSaveAsync(
        string projectDirectory,
        ProjectState state,
        bool allowUnconfiguredLockSources
    )
    {
        var normalizedState = NormalizeState(state);
        var resourceLimits = await ResolveSaveResourceLimitsAsync(
            projectDirectory,
            normalizedState
        );
        if (resourceLimits.Value is not { } effectiveResourceLimits)
        {
            return ManifestOperationResult<PreparedProjectState>.Failure(
                resourceLimits.Error!,
                resourceLimits.ErrorKind
            );
        }

        normalizedState = normalizedState with
        {
            EffectiveResourceLimits = effectiveResourceLimits,
        };
        var validation = await ValidateStateForSaveAsync(
            normalizedState,
            allowUnconfiguredLockSources
        );
        if (!validation.IsSuccess)
        {
            return ManifestOperationResult<PreparedProjectState>.Failure(
                validation.Error!,
                validation.ErrorKind
            );
        }

        var serialized = SerializeState(normalizedState);
        return serialized.Value is { } contents
            ? ManifestOperationResult<PreparedProjectState>.Success(
                new PreparedProjectState(normalizedState, contents)
            )
            : ManifestOperationResult<PreparedProjectState>.Failure(
                serialized.Error!,
                serialized.ErrorKind
            );
    }

    private async Task<ManifestOperationResult<ResourceLimits>> ResolveSaveResourceLimitsAsync(
        string projectDirectory,
        ProjectState state
    )
    {
        var local = state.Configuration.Preferences?.ResourceLimits;
        if (userSettingsStore is null)
        {
            return ManifestOperationResult<ResourceLimits>.Success(
                ResourceLimits.Resolve(local, state.EffectiveResourceLimits)
            );
        }

        var preferences = await LoadUserResourceLimitPreferencesAsync(projectDirectory);
        return preferences.Value is { } userPreferences
            ? ManifestOperationResult<ResourceLimits>.Success(
                ResourceLimits.Resolve(local, userPreferences.UserProject, userPreferences.Global)
            )
            : ManifestOperationResult<ResourceLimits>.Failure(
                preferences.Error!,
                preferences.ErrorKind
            );
    }

    private static async Task<ManifestOperationResult<bool>> ValidateStateForSaveAsync(
        ProjectState state,
        bool allowUnconfiguredLockSources
    )
    {
        var lockFileIssues = await ValidateAsync(state.LockFile, ManifestModelValidator.Validate);
        var configurationIssues = await ValidateAsync(
            state.Configuration,
            ManifestModelValidator.Validate
        );
        if (lockFileIssues.Count > 0 || configurationIssues.Count > 0)
        {
            var documentErrors = new List<string>();
            if (configurationIssues.Count > 0)
            {
                documentErrors.Add(
                    FormatValidationIssues(ConfigurationFileName, configurationIssues)
                );
            }

            if (lockFileIssues.Count > 0)
            {
                documentErrors.Add(FormatValidationIssues(LockFileName, lockFileIssues));
            }

            return ManifestOperationResult<bool>.Failure(
                $"Cannot write project state: {string.Join("; ", documentErrors)}"
            );
        }

        var validationError = ValidateState(
            state.Configuration,
            state.LockFile,
            state.EffectiveResourceLimits,
            allowUnconfiguredLockSources
        );
        return validationError;
    }

    private static ManifestOperationResult<SerializedProjectState> SerializeState(
        ProjectState state
    )
    {
        var serializedConfiguration = SerializeBounded(
            state.Configuration,
            state.EffectiveResourceLimits.ProjectManifestBytes,
            ConfigurationFileName
        );
        if (serializedConfiguration.Value is not { } configuration)
        {
            return ManifestOperationResult<SerializedProjectState>.Failure(
                serializedConfiguration.Error!,
                serializedConfiguration.ErrorKind
            );
        }

        var serializedLockFile = SerializeBounded(
            state.LockFile,
            state.EffectiveResourceLimits.LockFileBytes,
            LockFileName
        );
        if (serializedLockFile.Value is not { } lockFile)
        {
            return ManifestOperationResult<SerializedProjectState>.Failure(
                serializedLockFile.Error!,
                serializedLockFile.ErrorKind
            );
        }

        var validated = ValidateSerializedSizes(
            configuration,
            lockFile,
            state.EffectiveResourceLimits
        );
        if (!validated.IsSuccess)
        {
            return ManifestOperationResult<SerializedProjectState>.Failure(
                validated.Error!,
                validated.ErrorKind
            );
        }

        return ManifestOperationResult<SerializedProjectState>.Success(
            new SerializedProjectState(configuration, lockFile)
        );
    }

    private static ManifestOperationResult<string> SerializeBounded(
        object value,
        long byteLimit,
        string inputName
    )
    {
        using var writer = new BoundedTextWriter(byteLimit, inputName);
        try
        {
            _serializer.Serialize(writer, value);
            return ManifestOperationResult<string>.Success(writer.ToString());
        }
        catch (BoundedTextWriteException exception)
        {
            return ManifestOperationResult<string>.Failure(
                exception.Message,
                ManifestOperationErrorKind.ResourceLimitExceeded
            );
        }
    }

    private static ManifestOperationResult<bool> ValidateSerializedSizes(
        string configuration,
        string lockFile,
        ResourceLimits resourceLimits
    )
    {
        var configurationByteCount = Encoding.UTF8.GetByteCount(configuration);
        if (configurationByteCount > resourceLimits.ProjectManifestBytes)
        {
            return ManifestOperationResult<bool>.Failure(
                $"{ConfigurationFileName} serialized content exceeds its {resourceLimits.ProjectManifestBytes} byte limit ({configurationByteCount} bytes).",
                ManifestOperationErrorKind.ResourceLimitExceeded
            );
        }

        var lockFileByteCount = Encoding.UTF8.GetByteCount(lockFile);
        return lockFileByteCount > resourceLimits.LockFileBytes
            ? ManifestOperationResult<bool>.Failure(
                $"{LockFileName} serialized content exceeds its {resourceLimits.LockFileBytes} byte limit ({lockFileByteCount} bytes).",
                ManifestOperationErrorKind.ResourceLimitExceeded
            )
            : ManifestOperationResult<bool>.Success(true);
    }

    private async Task<
        ManifestOperationResult<IReadOnlyList<DocumentSnapshot>>
    > CreateSnapshotsAsync(
        string configurationPath,
        string lockFilePath,
        ResourceLimits resourceLimits
    )
    {
        var configurationSnapshot = await CreateSnapshotAsync(
            configurationPath,
            ConfigurationFileName,
            resourceLimits.ProjectManifestBytes
        );
        if (configurationSnapshot.Value is not { } configurationContentsSnapshot)
        {
            return ManifestOperationResult<IReadOnlyList<DocumentSnapshot>>.Failure(
                configurationSnapshot.Error!,
                configurationSnapshot.ErrorKind
            );
        }

        var lockFileSnapshot = await CreateSnapshotAsync(
            lockFilePath,
            LockFileName,
            resourceLimits.LockFileBytes
        );
        if (lockFileSnapshot.Value is not { } lockFileContentsSnapshot)
        {
            return ManifestOperationResult<IReadOnlyList<DocumentSnapshot>>.Failure(
                lockFileSnapshot.Error!,
                lockFileSnapshot.ErrorKind
            );
        }

        return ManifestOperationResult<IReadOnlyList<DocumentSnapshot>>.Success([
            configurationContentsSnapshot,
            lockFileContentsSnapshot,
        ]);
    }

    private async Task<ManifestOperationResult<DocumentSnapshot>> CreateSnapshotAsync(
        string path,
        string fileName,
        long byteLimit
    )
    {
        if (!_fileSystem.File.Exists(path))
        {
            return ManifestOperationResult<DocumentSnapshot>.Success(
                new DocumentSnapshot(path, null)
            );
        }

        try
        {
            var read = await BoundedFileReader.ReadTextAsync(
                _fileSystem,
                path,
                byteLimit,
                fileName
            );
            return read.Value is { } contents
                ? ManifestOperationResult<DocumentSnapshot>.Success(
                    new DocumentSnapshot(path, contents.Text)
                )
                : ManifestOperationResult<DocumentSnapshot>.Failure(read.Error!, read.ErrorKind);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return ManifestOperationResult<DocumentSnapshot>.Failure(
                $"Unable to read {fileName} before writing project state: {exception.Message}"
            );
        }
    }

    private static string CreateTemporaryPath(string documentPath) =>
        $"{documentPath}.{Guid.NewGuid():N}.tmp";

    private void DeleteTemporaryFile(string path)
    {
        if (_fileSystem.File.Exists(path))
        {
            _fileSystem.File.Delete(path);
        }
    }

    private string GetDocumentPath(string projectDirectory, string fileName) =>
        _fileSystem.Path.Combine(projectDirectory, fileName);

    private async Task<
        ManifestOperationResult<LoadedDocument<TDocument>>
    > LoadDocumentAsync<TDocument>(
        string projectDirectory,
        string fileName,
        Func<TDocument, IReadOnlyList<string>> validate,
        long byteLimit,
        Func<string, ManifestOperationResult<bool>>? preDeserialize = null
    )
        where TDocument : class
    {
        var documentPath = GetDocumentPath(projectDirectory, fileName);
        if (!_fileSystem.File.Exists(documentPath))
        {
            return ManifestOperationResult<LoadedDocument<TDocument>>.Failure(
                $"Missing {fileName} in '{projectDirectory}'."
            );
        }

        try
        {
            var read = await BoundedFileReader.ReadTextAsync(
                _fileSystem,
                documentPath,
                byteLimit,
                fileName
            );
            if (read.Value is not { } contents)
            {
                return ManifestOperationResult<LoadedDocument<TDocument>>.Failure(read.Error!);
            }

            var unknownResourceLimit =
                typeof(TDocument) == typeof(ProjectConfiguration)
                    ? ResourceLimitYamlValidator.ValidateProject(contents.Text)
                    : null;
            if (unknownResourceLimit is not null)
            {
                return ManifestOperationResult<LoadedDocument<TDocument>>.Failure(
                    $"Invalid {fileName} in '{projectDirectory}': {unknownResourceLimit}"
                );
            }

            var preflight = preDeserialize?.Invoke(contents.Text);
            if (preflight is { IsSuccess: false })
            {
                return ManifestOperationResult<LoadedDocument<TDocument>>.Failure(
                    preflight.Error!,
                    preflight.ErrorKind
                );
            }

            var document = _deserializer.Deserialize<TDocument>(contents.Text);
            if (document is null || !await IsValidAsync(document, validate))
            {
                return ManifestOperationResult<LoadedDocument<TDocument>>.Failure(
                    $"Invalid {fileName} in '{projectDirectory}'."
                );
            }

            return ManifestOperationResult<LoadedDocument<TDocument>>.Success(
                new LoadedDocument<TDocument>(document, contents.ByteCount)
            );
        }
        catch (Exception exception)
            when (exception
                    is IOException
                        or UnauthorizedAccessException
                        or YamlDotNet.Core.YamlException
            )
        {
            return ManifestOperationResult<LoadedDocument<TDocument>>.Failure(
                $"Unable to read {fileName} in '{projectDirectory}': {exception.Message}"
            );
        }
    }

    private async Task<
        ManifestOperationResult<UserResourceLimitPreferences>
    > LoadUserResourceLimitPreferencesAsync(string projectDirectory)
    {
        if (userSettingsStore is null)
        {
            return ManifestOperationResult<UserResourceLimitPreferences>.Success(
                new UserResourceLimitPreferences(null, null)
            );
        }

        var settings = await userSettingsStore.LoadAsync();
        if (settings.Value is not { } userSettings)
        {
            return ManifestOperationResult<UserResourceLimitPreferences>.Failure(settings.Error!);
        }

        var projectKey = userSettingsStore.GetProjectKey(projectDirectory);
        if (projectKey.Value is not { } key)
        {
            return ManifestOperationResult<UserResourceLimitPreferences>.Failure(projectKey.Error!);
        }

        userSettings.Projects.TryGetValue(key, out var projectSettings);
        return ManifestOperationResult<UserResourceLimitPreferences>.Success(
            new UserResourceLimitPreferences(
                userSettings.Global.Preferences?.ResourceLimits,
                projectSettings?.Preferences?.ResourceLimits
            )
        );
    }

    private static async Task<bool> IsValidAsync<TDocument>(
        TDocument document,
        Func<TDocument, IReadOnlyList<string>> validate
    )
        where TDocument : class
    {
        return (await ValidateAsync(document, validate)).Count == 0;
    }

    private static Task<IReadOnlyList<string>> ValidateAsync<TDocument>(
        TDocument document,
        Func<TDocument, IReadOnlyList<string>> validate
    )
        where TDocument : class => Task.FromResult(validate(document));

    private static string FormatValidationIssues(
        string documentName,
        IReadOnlyList<string> issues
    ) => $"{documentName} validation failed: {string.Join("; ", issues)}";

    private static ManifestOperationResult<bool> ValidateState(
        ProjectConfiguration configuration,
        ProjectLockFile lockFile,
        ResourceLimits resourceLimits,
        bool allowUnconfiguredLockSources = false
    )
    {
        var resolvedPacksById = new Dictionary<string, ProjectLockFile.ResolvedPack>(
            StringComparer.Ordinal
        );
        var validationError = ValidateResolvedPacks(
            configuration,
            lockFile,
            resolvedPacksById,
            allowUnconfiguredLockSources
        );
        if (validationError is not null)
        {
            return ManifestOperationResult<bool>.Failure(validationError);
        }

        var linkValidationError = ValidateLinks(
            configuration,
            lockFile,
            allowUnconfiguredLockSources
        );
        if (linkValidationError is not null)
        {
            return ManifestOperationResult<bool>.Failure(linkValidationError);
        }

        return ValidateRequestedRoots(configuration, lockFile, resolvedPacksById, resourceLimits);
    }

    private static string? ValidateLinks(
        ProjectConfiguration configuration,
        ProjectLockFile lockFile,
        bool allowUnconfiguredLockSources
    )
    {
        foreach (var linkName in configuration.Links.Keys)
        {
            var duplicatesRequestedPackId = configuration.Packs.Any(pack =>
                string.Equals(pack.Id, linkName, StringComparison.Ordinal)
            );
            if (duplicatesRequestedPackId)
            {
                return $"Project configuration uses '{linkName}' as both a link name and a requested pack ID.";
            }
        }

        foreach (var (linkName, resolvedLink) in lockFile.Links)
        {
            if (!configuration.Links.ContainsKey(linkName))
            {
                return $"Lock file contains link '{linkName}' that is not defined in the project configuration.";
            }

            var usesUnconfiguredSource =
                !allowUnconfiguredLockSources
                && !configuration.Sources.Any(source =>
                    string.Equals(source.Name, resolvedLink.SourceName, StringComparison.Ordinal)
                    && ConfiguredSourceIdentity.Create(source) == resolvedLink.SourceIdentity
                );
            if (usesUnconfiguredSource)
            {
                return "Lock file contains a source that is not configured.";
            }

            var targetPaths = new HashSet<string>(StringComparer.Ordinal);
            foreach (var file in resolvedLink.Files)
            {
                if (!targetPaths.Add(file.TargetPath))
                {
                    return $"Lock file link '{linkName}' contains duplicate target path '{file.TargetPath}'.";
                }
            }
        }

        return null;
    }

    private static ProjectState NormalizeState(ProjectState state) =>
        new()
        {
            Configuration = NormalizeConfiguration(state.Configuration),
            LockFile = NormalizeLockFile(state.LockFile),
            EffectiveResourceLimits = state.EffectiveResourceLimits,
        };

    private static ProjectConfiguration NormalizeConfiguration(
        ProjectConfiguration configuration
    ) =>
        configuration with
        {
            Links = configuration.Links.ToDictionary(
                link => link.Key,
                link => NormalizeLink(link.Value),
                StringComparer.Ordinal
            ),
            Packs = [.. configuration.Packs.Select(NormalizeRequestedPack)],
            Remap = NormalizeRemapping(configuration.Remap),
            Sources = [.. configuration.Sources.Select(NormalizeSource)],
        };

    private static ProjectLockFile NormalizeLockFile(ProjectLockFile lockFile) =>
        lockFile with
        {
            Links = lockFile.Links.ToDictionary(
                link => link.Key,
                link => NormalizeResolvedLink(link.Value),
                StringComparer.Ordinal
            ),
            Packs = [.. lockFile.Packs.Select(NormalizeResolvedPack)],
        };

    private static ProjectConfiguration.Link NormalizeLink(ProjectConfiguration.Link link) =>
        link with
        {
            Excludes = [.. link.Excludes.Select(ProjectPath.Normalize)],
            Includes = [.. link.Includes.Select(ProjectPath.Normalize)],
            Path = ProjectPath.NormalizeOptional(link.Path),
            StripPrefix = ProjectPath.NormalizeOptional(link.StripPrefix),
            Target = ProjectPath.NormalizeOptional(link.Target),
        };

    private static ProjectLockFile.ResolvedLink NormalizeResolvedLink(
        ProjectLockFile.ResolvedLink resolvedLink
    )
    {
        var normalizedLink = resolvedLink with
        {
            Files = [.. resolvedLink.Files.Select(NormalizeLinkFile)],
            GitSource = NormalizeGitSource(resolvedLink.GitSource),
        };

        return resolvedLink.SourceIdentity is { } sourceIdentity
            ? normalizedLink with
            {
                SourceIdentity = NormalizeSourceIdentity(sourceIdentity),
            }
            : normalizedLink;
    }

    private static ProjectLockFile.LinkFile NormalizeLinkFile(ProjectLockFile.LinkFile file) =>
        file with
        {
            DeclaredTargetPath = ProjectPath.Normalize(file.DeclaredTargetPath),
            SourcePath = ProjectPath.Normalize(file.SourcePath),
            TargetPath = ProjectPath.Normalize(file.TargetPath),
        };

    private static ProjectConfiguration.RequestedPack NormalizeRequestedPack(
        ProjectConfiguration.RequestedPack pack
    ) =>
        pack with
        {
            Destination = ProjectPath.NormalizeOptional(pack.Destination),
            Remap = NormalizeRemapping(pack.Remap),
        };

    private static ProjectConfiguration.Remapping? NormalizeRemapping(
        ProjectConfiguration.Remapping? remapping
    ) =>
        remapping is null
            ? null
            : new ProjectConfiguration.Remapping
            {
                Directories = NormalizeMappings(remapping.Directories),
                Files = NormalizeMappings(remapping.Files),
            };

    private static ProjectConfiguration.Source NormalizeSource(
        ProjectConfiguration.Source source
    ) =>
        source switch
        {
            ProjectConfiguration.GitSource gitSource => gitSource with
            {
                Path = ProjectPath.NormalizeOptional(gitSource.Path),
            },
            ProjectConfiguration.LocalSource localSource => localSource with
            {
                Path = ProjectPath.Normalize(localSource.Path),
            },
            ProjectConfiguration.OciSource ociSource => NormalizeOciSource(ociSource),
            _ => source,
        };

    private static ProjectConfiguration.OciSource NormalizeOciSource(
        ProjectConfiguration.OciSource source
    )
    {
        var location = SourceIdentityNormalizer.NormalizeOciLocation(source.Url, source.Path);
        return location.Value is { } normalized
            ? source with
            {
                Url = normalized.Url,
                Path = normalized.Path,
                Username = source.Username?.Trim(),
            }
            : source;
    }

    private static ProjectLockFile.ResolvedPack NormalizeResolvedPack(
        ProjectLockFile.ResolvedPack pack
    ) =>
        pack with
        {
            Destination = ProjectPath.NormalizeOptional(pack.Destination),
            ExternalSources = pack.ExternalSources.ToDictionary(
                source => source.Key,
                source => source.Value,
                StringComparer.Ordinal
            ),
            GitSource = NormalizeGitSource(pack.GitSource),
            ManagedFiles = [.. pack.ManagedFiles.Select(NormalizeManagedFile)],
            PackPath = ProjectPath.Normalize(pack.PackPath),
            SourceIdentity = pack.SourceIdentity is { } sourceIdentity
                ? NormalizeSourceIdentity(sourceIdentity)
                : null,
            SourcePath = ProjectPath.NormalizeOptional(pack.SourcePath),
        };

    private static ConfiguredSourceIdentity NormalizeSourceIdentity(ConfiguredSourceIdentity source)
    {
        if (string.Equals(source.Type, SourceFingerprint.OciType, StringComparison.Ordinal))
        {
            var location = SourceIdentityNormalizer.NormalizeOciLocation(
                source.Url ?? string.Empty,
                source.Path
            );
            if (location.Value is { } normalized)
            {
                return source with { Url = normalized.Url, Path = normalized.Path, Ref = null };
            }
        }

        return source with
        {
            Path = ProjectPath.NormalizeOptional(source.Path),
        };
    }

    private static GitSourceProvenance? NormalizeGitSource(GitSourceProvenance? gitSource) =>
        gitSource is null
            ? null
            : gitSource with
            {
                Path = ProjectPath.NormalizeOptional(gitSource.Path),
            };

    private static ProjectLockFile.ManagedFile NormalizeManagedFile(
        ProjectLockFile.ManagedFile managedFile
    ) =>
        managedFile with
        {
            DeclaredTargetPath = ProjectPath.NormalizeOptional(managedFile.DeclaredTargetPath),
            SourcePath = ProjectPath.NormalizeOptional(managedFile.SourcePath),
            TargetPath = ProjectPath.Normalize(managedFile.TargetPath),
        };

    private static Dictionary<string, string> NormalizeMappings(
        IReadOnlyDictionary<string, string> mappings
    ) =>
        mappings.ToDictionary(
            mapping => ProjectPath.Normalize(mapping.Key),
            mapping => ProjectPath.Normalize(mapping.Value),
            StringComparer.Ordinal
        );

    private static string? ValidateResolvedPacks(
        ProjectConfiguration configuration,
        ProjectLockFile lockFile,
        IDictionary<string, ProjectLockFile.ResolvedPack> resolvedPacksById,
        bool allowUnconfiguredLockSources
    )
    {
        foreach (var resolvedPack in lockFile.Packs)
        {
            if (!resolvedPacksById.TryAdd(resolvedPack.Id, resolvedPack))
            {
                return $"Lock file contains multiple resolved packs with ID '{resolvedPack.Id}'.";
            }

            var usesUnconfiguredSource =
                !allowUnconfiguredLockSources
                && !MatchesConfiguredSourceIdentity(configuration.Sources, resolvedPack);
            if (usesUnconfiguredSource)
            {
                return "Lock file contains a source that is not configured.";
            }
        }

        return null;
    }

    private static bool MatchesConfiguredSourceIdentity(
        IReadOnlyList<ProjectConfiguration.Source> configuredSources,
        ProjectLockFile.ResolvedPack resolvedPack
    ) =>
        resolvedPack.SourceIdentity is { } identity
        && configuredSources.Any(source =>
            string.Equals(source.Name, resolvedPack.SourceName, StringComparison.Ordinal)
            && ConfiguredSourceIdentity.Create(source) == identity
        );

    private static ManifestOperationResult<bool> ValidateRequestedRoots(
        ProjectConfiguration configuration,
        ProjectLockFile lockFile,
        Dictionary<string, ProjectLockFile.ResolvedPack> resolvedPacksById,
        ResourceLimits resourceLimits
    )
    {
        if (resolvedPacksById.Count > resourceLimits.DependencyNodes)
        {
            return ManifestOperationResult<bool>.Failure(
                $"Lock file contains at least {resolvedPacksById.Count} unique packs, exceeding its {resourceLimits.DependencyNodes} node limit.",
                ManifestOperationErrorKind.ResourceLimitExceeded
            );
        }

        var roots = ValidateRequestedRootDeclarations(configuration, resolvedPacksById);
        if (roots.Value is not { } requestedRoots)
        {
            return ManifestOperationResult<bool>.Failure(roots.Error!, roots.ErrorKind);
        }

        var graph = BuildLockGraph(resolvedPacksById, lockFile.Links, resourceLimits);
        return graph.Value is { } lockGraph
            ? ValidateLockGraph(resolvedPacksById, requestedRoots, lockGraph, resourceLimits)
            : ManifestOperationResult<bool>.Failure(graph.Error!, graph.ErrorKind);
    }

    private static ManifestOperationResult<HashSet<string>> ValidateRequestedRootDeclarations(
        ProjectConfiguration configuration,
        Dictionary<string, ProjectLockFile.ResolvedPack> resolvedPacksById
    )
    {
        var requestedRoots = new HashSet<string>(StringComparer.Ordinal);
        foreach (var requestedPack in configuration.Packs)
        {
            if (!requestedRoots.Add(requestedPack.Id))
            {
                return ManifestOperationResult<HashSet<string>>.Failure(
                    $"Project configuration contains duplicate requested pack '{requestedPack.Id}'."
                );
            }

            if (!resolvedPacksById.TryGetValue(requestedPack.Id, out var resolvedPack))
            {
                return ManifestOperationResult<HashSet<string>>.Failure(
                    $"Lock file does not contain requested pack '{requestedPack.Id}'."
                );
            }

            var hasMismatchedVersion =
                requestedPack.Version is not null
                && !string.Equals(
                    requestedPack.Version,
                    resolvedPack.Version,
                    StringComparison.Ordinal
                );
            if (hasMismatchedVersion)
            {
                return ManifestOperationResult<HashSet<string>>.Failure(
                    $"Lock file version for '{requestedPack.Id}' does not match the requested version."
                );
            }

            var hasMismatchedDestination = !string.Equals(
                requestedPack.Destination,
                resolvedPack.Destination,
                StringComparison.Ordinal
            );
            if (hasMismatchedDestination)
            {
                return ManifestOperationResult<HashSet<string>>.Failure(
                    $"Lock file destination for '{requestedPack.Id}' does not match the requested destination."
                );
            }
        }

        return ManifestOperationResult<HashSet<string>>.Success(requestedRoots);
    }

    private static ManifestOperationResult<LockGraph> BuildLockGraph(
        Dictionary<string, ProjectLockFile.ResolvedPack> resolvedPacksById,
        IReadOnlyDictionary<string, ProjectLockFile.ResolvedLink> links,
        ResourceLimits resourceLimits
    )
    {
        var fileCounts = ValidateLockFileCounts(resolvedPacksById.Values, links, resourceLimits);
        if (!fileCounts.IsSuccess)
        {
            return ManifestOperationResult<LockGraph>.Failure(
                fileCounts.Error!,
                fileCounts.ErrorKind
            );
        }

        var dependencies = resolvedPacksById.Keys.ToDictionary(
            id => id,
            _ => new List<string>(),
            StringComparer.Ordinal
        );
        var indegree = resolvedPacksById.Keys.ToDictionary(
            id => id,
            _ => 0L,
            StringComparer.Ordinal
        );
        long traversedReferences = 0;
        foreach (var pack in resolvedPacksById.Values)
        {
            if (
                pack.OciSource?.ContentSize is { } contentSize
                && contentSize > resourceLimits.OciDownloadBytes
            )
            {
                return ManifestOperationResult<LockGraph>.Failure(
                    $"Lock file OCI pack '{pack.Id}' declares at least {contentSize} content bytes, exceeding its {resourceLimits.OciDownloadBytes} byte download limit.",
                    ManifestOperationErrorKind.ResourceLimitExceeded
                );
            }

            foreach (var reference in pack.Packs)
            {
                traversedReferences++;
                if (traversedReferences > resourceLimits.DependencyReferences)
                {
                    return ManifestOperationResult<LockGraph>.Failure(
                        $"Lock file graph traversed at least {traversedReferences} references, exceeding its {resourceLimits.DependencyReferences} reference limit.",
                        ManifestOperationErrorKind.ResourceLimitExceeded
                    );
                }

                if (!resolvedPacksById.TryGetValue(reference.Id, out var dependency))
                {
                    return ManifestOperationResult<LockGraph>.Failure(
                        $"Lock file reference '{reference.Id}@{reference.Version}' is unavailable."
                    );
                }

                if (!string.Equals(reference.Version, dependency.Version, StringComparison.Ordinal))
                {
                    return ManifestOperationResult<LockGraph>.Failure(
                        $"Lock file reference '{reference.Id}' has a conflicting version."
                    );
                }

                dependencies[pack.Id].Add(dependency.Id);
                indegree[dependency.Id]++;
            }
        }

        return ManifestOperationResult<LockGraph>.Success(new LockGraph(dependencies, indegree));
    }

    private static ManifestOperationResult<bool> ValidateLockFileCounts(
        IEnumerable<ProjectLockFile.ResolvedPack> packs,
        IReadOnlyDictionary<string, ProjectLockFile.ResolvedLink> links,
        ResourceLimits resourceLimits
    )
    {
        long graphFiles = 0;
        foreach (var pack in packs)
        {
            if (pack.ManagedFiles.Count > resourceLimits.FilesPerPack)
            {
                return ManifestOperationResult<bool>.Failure(
                    $"Lock file pack '{pack.Id}' contains at least {pack.ManagedFiles.Count} managed files, exceeding its {resourceLimits.FilesPerPack} file limit.",
                    ManifestOperationErrorKind.ResourceLimitExceeded
                );
            }

            graphFiles += pack.ManagedFiles.Count;
            if (graphFiles > resourceLimits.FilesPerGraph)
            {
                return ManifestOperationResult<bool>.Failure(
                    $"Lock file graph contains at least {graphFiles} managed files, exceeding its {resourceLimits.FilesPerGraph} file limit.",
                    ManifestOperationErrorKind.ResourceLimitExceeded
                );
            }
        }

        foreach (var (linkName, link) in links)
        {
            if (link.Files.Count > resourceLimits.FilesPerPack)
            {
                return ManifestOperationResult<bool>.Failure(
                    $"Lock file link '{linkName}' contains at least {link.Files.Count} files, exceeding its {resourceLimits.FilesPerPack} file limit.",
                    ManifestOperationErrorKind.ResourceLimitExceeded
                );
            }

            graphFiles = checked(graphFiles + link.Files.Count);
            if (graphFiles > resourceLimits.FilesPerGraph)
            {
                return ManifestOperationResult<bool>.Failure(
                    $"Lock file graph contains at least {graphFiles} managed and linked files, exceeding its {resourceLimits.FilesPerGraph} file limit.",
                    ManifestOperationErrorKind.ResourceLimitExceeded
                );
            }
        }

        return ManifestOperationResult<bool>.Success(true);
    }

    private static ManifestOperationResult<bool> ValidateLockGraph(
        Dictionary<string, ProjectLockFile.ResolvedPack> resolvedPacksById,
        HashSet<string> requestedRoots,
        LockGraph graph,
        ResourceLimits resourceLimits
    )
    {
        var indegree = graph.Indegree.ToDictionary(
            pair => pair.Key,
            pair => pair.Value,
            StringComparer.Ordinal
        );
        var queue = new Queue<string>(
            indegree.Where(pair => pair.Value == 0).Select(pair => pair.Key)
        );
        var topologicalOrder = new List<string>();
        while (queue.TryDequeue(out var packId))
        {
            topologicalOrder.Add(packId);
            foreach (var dependencyId in graph.Dependencies[packId])
            {
                indegree[dependencyId]--;
                if (indegree[dependencyId] == 0)
                {
                    queue.Enqueue(dependencyId);
                }
            }
        }

        if (topologicalOrder.Count != resolvedPacksById.Count)
        {
            var cyclePackId = indegree.First(pair => pair.Value > 0).Key;
            var cyclePack = resolvedPacksById[cyclePackId];
            return ManifestOperationResult<bool>.Failure(
                $"Lock file contains a dependency cycle at '{cyclePackId}@{cyclePack.Version}'."
            );
        }

        var depths = requestedRoots.ToDictionary(id => id, _ => 0L, StringComparer.Ordinal);
        foreach (var packId in topologicalOrder)
        {
            if (!depths.TryGetValue(packId, out var depth))
            {
                continue;
            }

            foreach (var dependencyId in graph.Dependencies[packId])
            {
                var observedDepth = checked(depth + 1);
                if (observedDepth > resourceLimits.DependencyDepth)
                {
                    return ManifestOperationResult<bool>.Failure(
                        $"Lock file graph reference '{packId}' to '{dependencyId}' exceeds its {resourceLimits.DependencyDepth} edge-depth limit (at least {observedDepth} edges observed).",
                        ManifestOperationErrorKind.ResourceLimitExceeded
                    );
                }

                if (
                    !depths.TryGetValue(dependencyId, out var existingDepth)
                    || observedDepth > existingDepth
                )
                {
                    depths[dependencyId] = observedDepth;
                }
            }
        }

        return depths.Count == resolvedPacksById.Count
            ? ManifestOperationResult<bool>.Success(true)
            : ManifestOperationResult<bool>.Failure(
                "Lock file contains packs that are unreachable from requested packs."
            );
    }

    private void RestoreSnapshots(
        string projectDirectory,
        IReadOnlyList<DocumentSnapshot> snapshots
    )
    {
        foreach (var snapshot in snapshots)
        {
            if (snapshot.Content is null)
            {
                ProjectMutationPathSecurity.EnsureNoAliases(
                    _fileSystem,
                    projectDirectory,
                    snapshot.Path
                );
                if (_fileSystem.File.Exists(snapshot.Path))
                {
                    _fileSystem.File.Delete(snapshot.Path);
                }

                continue;
            }

            ProjectMutationPathSecurity.ReplaceFile(
                _fileSystem,
                projectDirectory,
                snapshot.Path,
                Encoding.UTF8.GetBytes(snapshot.Content)
            );
        }
    }

    private sealed record DocumentSnapshot(string Path, string? Content);

    private sealed record SerializedProjectState(string Configuration, string LockFile);

    private sealed record PreparedProjectState(ProjectState State, SerializedProjectState Contents);

    private sealed record LockGraph(
        Dictionary<string, List<string>> Dependencies,
        Dictionary<string, long> Indegree
    );

    private sealed record LoadedDocument<TDocument>(TDocument Document, long ByteCount)
        where TDocument : class;

    private sealed record UserResourceLimitPreferences(
        ResourceLimitPreferences? Global,
        ResourceLimitPreferences? UserProject
    );

    private sealed class BoundedTextWriter(long byteLimit, string inputName) : TextWriter
    {
        private static readonly UTF8Encoding _utf8 = new(
            encoderShouldEmitUTF8Identifier: false,
            throwOnInvalidBytes: true
        );
        private readonly StringBuilder _contents = new();
        private long _byteCount;

        public override Encoding Encoding => _utf8;

        public override void Write(char value) => Write(value.ToString().AsSpan());

        public override void Write(char[] buffer, int index, int count) =>
            Write(buffer.AsSpan(index, count));

        public override void Write(string? value)
        {
            if (value is not null)
            {
                Write(value.AsSpan());
            }
        }

        public override void Write(ReadOnlySpan<char> buffer)
        {
            var observedBytes = checked(_byteCount + _utf8.GetByteCount(buffer));
            if (observedBytes > byteLimit)
            {
                throw new BoundedTextWriteException(
                    $"{inputName} serialized content exceeds its {byteLimit} byte limit (at least {observedBytes} bytes produced)."
                );
            }

            _contents.Append(buffer);
            _byteCount = observedBytes;
        }

        public override string ToString() => _contents.ToString();
    }

    private sealed class BoundedTextWriteException(string message) : Exception(message);
}
