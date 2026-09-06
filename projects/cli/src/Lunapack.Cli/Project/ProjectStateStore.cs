using System.IO.Abstractions;
using System.Text;
using Lunapack.Cli.Application.CommandExecution;
using Lunapack.Cli.Application.Paths;
using Lunapack.Cli.Application.Serialization;
using Lunapack.Cli.Packs.Manifest;
using Lunapack.Cli.Sources;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Lunapack.Cli.Project;

internal sealed class ProjectStateStore(IFileSystem fileSystem) : IProjectStateStore
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
        var configuration = await LoadDocumentAsync<ProjectConfiguration>(
            projectDirectory,
            ConfigurationFileName,
            ManifestModelValidator.Validate
        );
        if (configuration.Value is not { } loadedConfiguration)
        {
            return ManifestOperationResult<ProjectState>.Failure(
                configuration.Error ?? "Unable to load project configuration."
            );
        }

        var lockFile = await LoadDocumentAsync<ProjectLockFile>(
            projectDirectory,
            LockFileName,
            ManifestModelValidator.Validate
        );
        if (lockFile.Value is not { } loadedLockFile)
        {
            return ManifestOperationResult<ProjectState>.Failure(
                lockFile.Error ?? "Unable to load project lock file."
            );
        }

        var migratedLockFile = ProjectLockFileMigration.ToCurrent(
            loadedConfiguration,
            loadedLockFile
        );
        if (migratedLockFile.Value is not { } currentLockFile)
        {
            return ManifestOperationResult<ProjectState>.Failure(
                migratedLockFile.Error ?? "Unable to migrate project lock file."
            );
        }

        var normalizedState = NormalizeState(
            new ProjectState { Configuration = loadedConfiguration, LockFile = currentLockFile }
        );
        var validationError = ValidateState(
            normalizedState.Configuration,
            normalizedState.LockFile,
            allowUnconfiguredLockSources: true
        );
        if (validationError is not null)
        {
            return ManifestOperationResult<ProjectState>.Failure(validationError);
        }

        return ManifestOperationResult<ProjectState>.Success(
            HydrateSoleRootOwnership(normalizedState)
        );
    }

    public async Task<ManifestOperationResult<bool>> InitializeAsync(string projectDirectory)
    {
        var configuration = new ProjectConfiguration { SchemaVersion = 1 };
        var lockFile = new ProjectLockFile
        {
            SchemaVersion = ProjectLockFileMigration.CurrentSchemaVersion,
        };
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

        var temporaryConfigurationPath = CreateTemporaryPath(configurationPath);
        var temporaryLockFilePath = CreateTemporaryPath(lockFilePath);
        try
        {
            _fileSystem.File.WriteAllText(
                temporaryConfigurationPath,
                _serializer.Serialize(
                    new InitialProjectConfiguration { SchemaVersion = configuration.SchemaVersion }
                )
            );
            _fileSystem.File.WriteAllText(
                temporaryLockFilePath,
                _serializer.Serialize(
                    new InitialProjectLockFile { SchemaVersion = lockFile.SchemaVersion }
                )
            );
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

    public async Task<ManifestOperationResult<bool>> SaveAsync(
        string projectDirectory,
        ProjectState state
    ) => await SaveAsync(projectDirectory, state, allowUnconfiguredLockSources: false);

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
        var preparedState = await PrepareStateForSaveAsync(state, allowUnconfiguredLockSources);
        if (preparedState.Value is not { } normalizedState)
        {
            return ManifestOperationResult<bool>.Failure(
                preparedState.Error ?? "Unable to prepare project state."
            );
        }

        var configurationPath = GetDocumentPath(projectDirectory, ConfigurationFileName);
        var lockFilePath = GetDocumentPath(projectDirectory, LockFileName);
        var snapshots = new[] { CreateSnapshot(configurationPath), CreateSnapshot(lockFilePath) };
        var temporaryConfigurationPath = CreateTemporaryPath(configurationPath);
        var temporaryLockFilePath = CreateTemporaryPath(lockFilePath);

        try
        {
            _fileSystem.File.WriteAllText(
                temporaryConfigurationPath,
                _serializer.Serialize(normalizedState.Configuration)
            );
            _fileSystem.File.WriteAllText(
                temporaryLockFilePath,
                _serializer.Serialize(normalizedState.LockFile)
            );

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

    private static async Task<ManifestOperationResult<ProjectState>> PrepareStateForSaveAsync(
        ProjectState state,
        bool allowUnconfiguredLockSources
    )
    {
        var hasInvalidInput =
            !await IsValidAsync(state.Configuration, ManifestModelValidator.Validate)
            || state.LockFile.SchemaVersion == 1
                && !await IsValidAsync(state.LockFile, ManifestModelValidator.Validate);
        if (hasInvalidInput)
        {
            return ManifestOperationResult<ProjectState>.Failure(
                "Refusing to write project state that does not match the schemas."
            );
        }

        var nameCollision = ValidateConfigurationNameCollisions(state.Configuration);
        if (nameCollision is not null)
        {
            return ManifestOperationResult<ProjectState>.Failure(nameCollision);
        }

        var normalizedState = NormalizeState(state);
        var migratedLockFile = ProjectLockFileMigration.PrepareForPersistence(
            normalizedState.Configuration,
            normalizedState.LockFile
        );
        if (migratedLockFile.Value is not { } currentLockFile)
        {
            return ManifestOperationResult<ProjectState>.Failure(
                migratedLockFile.Error ?? "Unable to migrate project lock file."
            );
        }

        normalizedState = normalizedState with { LockFile = currentLockFile };
        if (!await IsValidAsync(normalizedState.LockFile, ManifestModelValidator.Validate))
        {
            return ManifestOperationResult<ProjectState>.Failure(
                "Refusing to write project state that does not match the schemas."
            );
        }

        var validationError = ValidateState(
            normalizedState.Configuration,
            normalizedState.LockFile,
            allowUnconfiguredLockSources
        );
        return validationError is null
            ? ManifestOperationResult<ProjectState>.Success(normalizedState)
            : ManifestOperationResult<ProjectState>.Failure(validationError);
    }

    private DocumentSnapshot CreateSnapshot(string path) =>
        _fileSystem.File.Exists(path)
            ? new DocumentSnapshot(path, _fileSystem.File.ReadAllText(path))
            : new DocumentSnapshot(path, null);

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

    private async Task<ManifestOperationResult<TDocument>> LoadDocumentAsync<TDocument>(
        string projectDirectory,
        string fileName,
        Func<TDocument, IReadOnlyList<string>> validate
    )
        where TDocument : class
    {
        var documentPath = GetDocumentPath(projectDirectory, fileName);
        if (!_fileSystem.File.Exists(documentPath))
        {
            return ManifestOperationResult<TDocument>.Failure(
                $"Missing {fileName} in '{projectDirectory}'."
            );
        }

        try
        {
            var document = _deserializer.Deserialize<TDocument>(
                _fileSystem.File.ReadAllText(documentPath)
            );
            if (document is null || !await IsValidAsync(document, validate))
            {
                return ManifestOperationResult<TDocument>.Failure(
                    $"Invalid {fileName} in '{projectDirectory}'."
                );
            }

            return ManifestOperationResult<TDocument>.Success(document);
        }
        catch (Exception exception)
            when (exception
                    is IOException
                        or UnauthorizedAccessException
                        or YamlDotNet.Core.YamlException
            )
        {
            return ManifestOperationResult<TDocument>.Failure(
                $"Unable to read {fileName} in '{projectDirectory}': {exception.Message}"
            );
        }
    }

    private static async Task<bool> IsValidAsync<TDocument>(
        TDocument document,
        Func<TDocument, IReadOnlyList<string>> validate
    )
        where TDocument : class
    {
        return await Task.FromResult(validate(document).Count == 0);
    }

    private static string? ValidateState(
        ProjectConfiguration configuration,
        ProjectLockFile lockFile,
        bool allowUnconfiguredLockSources = false
    )
    {
        var resolvedPacksByKey =
            new Dictionary<ProjectLockFile.ResolvedPackKey, ProjectLockFile.ResolvedPack>();
        var validationError = ValidateResolvedPacks(
            configuration,
            lockFile,
            resolvedPacksByKey,
            allowUnconfiguredLockSources
        );
        if (validationError is not null)
        {
            return validationError;
        }

        var linkValidationError = ValidateLinks(
            configuration,
            lockFile,
            allowUnconfiguredLockSources
        );
        if (linkValidationError is not null)
        {
            return linkValidationError;
        }

        return ValidateRequestedRoots(configuration, lockFile, resolvedPacksByKey)
            ?? ValidateUniqueOwnership(lockFile);
    }

    private static string? ValidateLinks(
        ProjectConfiguration configuration,
        ProjectLockFile lockFile,
        bool allowUnconfiguredLockSources
    )
    {
        var nameCollision = ValidateConfigurationNameCollisions(configuration);
        if (nameCollision is not null)
        {
            return nameCollision;
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

    private static string? ValidateConfigurationNameCollisions(ProjectConfiguration configuration)
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

        return null;
    }

    private static ProjectState NormalizeState(ProjectState state) =>
        new()
        {
            Configuration = NormalizeConfiguration(state.Configuration),
            LockFile = NormalizeLockFile(state.LockFile),
        };

    private static ProjectState HydrateSoleRootOwnership(ProjectState state)
    {
        foreach (
            var instanceGroup in state.LockFile.Instances.GroupBy(instance =>
                instance.RootResolution
            )
        )
        {
            var instances = instanceGroup.ToList();
            var root = state.LockFile.Packs.SingleOrDefault(pack => pack.Key == instanceGroup.Key);
            if (instances.Count != 1 || root is null)
            {
                continue;
            }

            root.Destination = instances[0].Destination;
            root.ExternalSources = instances[0].ExternalSources;
            root.ManagedFiles = instances[0].ManagedFiles;
        }

        return state;
    }

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
            Instances = [.. lockFile.Instances.Select(NormalizePackInstance)],
            Packs = [.. lockFile.Packs.Select(NormalizeResolvedPack)],
        };

    private static ProjectLockFile.PackInstance NormalizePackInstance(
        ProjectLockFile.PackInstance instance
    ) =>
        instance with
        {
            Destination = ProjectPath.NormalizeOptional(instance.Destination),
            ExternalSources = instance.ExternalSources.ToDictionary(
                source => source.Key,
                source => source.Value,
                StringComparer.Ordinal
            ),
            ManagedFiles = [.. instance.ManagedFiles.Select(NormalizeManagedFile)],
            Placements = NormalizeMappings(instance.Placements),
            RootResolution = NormalizeResolvedPackKey(instance.RootResolution),
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
            _ => source,
        };

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
            Key = pack.Key is null ? null : NormalizeResolvedPackKey(pack.Key),
            PackPath = ProjectPath.Normalize(pack.PackPath),
            Packs =
            [
                .. pack.Packs.Select(reference =>
                    reference with
                    {
                        Resolution = reference.Resolution is null
                            ? null
                            : NormalizeResolvedPackKey(reference.Resolution),
                    }
                ),
            ],
            SourceIdentity = pack.SourceIdentity is { } sourceIdentity
                ? NormalizeSourceIdentity(sourceIdentity)
                : null,
            SourcePath = ProjectPath.NormalizeOptional(pack.SourcePath),
        };

    private static ProjectLockFile.ResolvedPackKey NormalizeResolvedPackKey(
        ProjectLockFile.ResolvedPackKey key
    ) => key with { SourceIdentity = NormalizeSourceIdentity(key.SourceIdentity) };

    private static ConfiguredSourceIdentity NormalizeSourceIdentity(
        ConfiguredSourceIdentity source
    ) => source with { Path = ProjectPath.NormalizeOptional(source.Path) };

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
        mappings
            .OrderBy(mapping => mapping.Key, StringComparer.Ordinal)
            .ToDictionary(
                mapping => ProjectPath.Normalize(mapping.Key),
                mapping => ProjectPath.Normalize(mapping.Value),
                StringComparer.Ordinal
            );

    private static string? ValidateResolvedPacks(
        ProjectConfiguration configuration,
        ProjectLockFile lockFile,
        Dictionary<
            ProjectLockFile.ResolvedPackKey,
            ProjectLockFile.ResolvedPack
        > resolvedPacksByKey,
        bool allowUnconfiguredLockSources
    )
    {
        foreach (var resolvedPack in lockFile.Packs)
        {
            if (resolvedPack.Key is not { } key || !resolvedPacksByKey.TryAdd(key, resolvedPack))
            {
                return $"Lock file contains a missing or duplicate resolution key for '{resolvedPack.Id}@{resolvedPack.Version}'.";
            }

            var keyDoesNotMatchPack =
                !string.Equals(key.Id, resolvedPack.Id, StringComparison.Ordinal)
                || !string.Equals(key.Version, resolvedPack.Version, StringComparison.Ordinal)
                || key.SourceIdentity != resolvedPack.SourceIdentity
                || !string.Equals(
                    key.ResolvedCommit,
                    resolvedPack.GitSource?.ResolvedCommit,
                    StringComparison.Ordinal
                );
            if (keyDoesNotMatchPack)
            {
                return $"Lock file resolution key for '{resolvedPack.Id}@{resolvedPack.Version}' does not match its resolved node.";
            }

            var usesUnconfiguredSource =
                !allowUnconfiguredLockSources
                && !MatchesConfiguredSourceIdentity(configuration.Sources, resolvedPack);
            if (usesUnconfiguredSource)
            {
                return "Lock file contains a source that is not configured.";
            }
        }

        foreach (var resolvedPack in lockFile.Packs)
        {
            foreach (var reference in resolvedPack.Packs)
            {
                if (
                    reference.Resolution is not { } resolution
                    || !resolvedPacksByKey.ContainsKey(resolution)
                    || !string.Equals(reference.Id, resolution.Id, StringComparison.Ordinal)
                    || !string.Equals(
                        reference.Version,
                        resolution.Version,
                        StringComparison.Ordinal
                    )
                )
                {
                    return $"Lock file reference '{reference.Id}@{reference.Version}' does not identify an exact resolved node.";
                }
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

    private static string? ValidateRequestedRoots(
        ProjectConfiguration configuration,
        ProjectLockFile lockFile,
        Dictionary<ProjectLockFile.ResolvedPackKey, ProjectLockFile.ResolvedPack> resolvedPacksByKey
    )
    {
        var instancesByIdentity =
            new Dictionary<PackInstanceIdentity, ProjectLockFile.PackInstance>();
        foreach (var instance in lockFile.Instances)
        {
            if (!instancesByIdentity.TryAdd(new(instance.Id, instance.Name), instance))
            {
                return $"Lock file contains duplicate pack instance '{instance.Id}/{instance.Name}'.";
            }
        }

        var reachablePacks = new HashSet<ProjectLockFile.ResolvedPackKey>();
        var visitingPacks = new HashSet<ProjectLockFile.ResolvedPackKey>();
        foreach (var requestedPack in configuration.Packs)
        {
            var identity = requestedPack.GetInstanceIdentity();
            if (!instancesByIdentity.Remove(identity, out var instance))
            {
                return $"Lock file does not contain requested pack instance '{identity.PackId}/{identity.Alias}'.";
            }

            if (!resolvedPacksByKey.TryGetValue(instance.RootResolution, out var resolvedPack))
            {
                return $"Lock file instance '{identity.PackId}/{identity.Alias}' has an unavailable root resolution.";
            }

            if (!string.Equals(instance.Id, resolvedPack.Id, StringComparison.Ordinal))
            {
                return $"Lock file instance '{identity.PackId}/{identity.Alias}' resolves a different pack ID.";
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
                return $"Lock file version for '{requestedPack.Id}' does not match the requested version.";
            }

            var hasMismatchedDestination = !string.Equals(
                requestedPack.Destination,
                instance.Destination,
                StringComparison.Ordinal
            );
            if (hasMismatchedDestination)
            {
                return $"Lock file destination for '{requestedPack.Id}' does not match the requested destination.";
            }

            var validationError = ValidateReachablePack(
                resolvedPack,
                resolvedPacksByKey,
                reachablePacks,
                visitingPacks
            );
            if (validationError is not null)
            {
                return validationError;
            }
        }

        if (instancesByIdentity.Count > 0)
        {
            return "Lock file contains pack instances that are not defined in project configuration.";
        }

        return reachablePacks.Count == resolvedPacksByKey.Count
            ? null
            : "Lock file contains packs that are unreachable from requested packs.";
    }

    private static string? ValidateReachablePack(
        ProjectLockFile.ResolvedPack pack,
        Dictionary<
            ProjectLockFile.ResolvedPackKey,
            ProjectLockFile.ResolvedPack
        > resolvedPacksByKey,
        ISet<ProjectLockFile.ResolvedPackKey> reachablePacks,
        ISet<ProjectLockFile.ResolvedPackKey> visitingPacks
    )
    {
        if (pack.Key is not { } key)
        {
            return $"Lock file resolved pack '{pack.Id}@{pack.Version}' has no resolution key.";
        }

        if (reachablePacks.Contains(key))
        {
            return null;
        }

        if (!visitingPacks.Add(key))
        {
            return $"Lock file contains a dependency cycle at '{pack.Id}@{pack.Version}'.";
        }

        foreach (var reference in pack.Packs)
        {
            if (
                reference.Resolution is not { } resolution
                || !resolvedPacksByKey.TryGetValue(resolution, out var dependency)
            )
            {
                return $"Lock file reference '{reference.Id}@{reference.Version}' is unavailable.";
            }

            if (!string.Equals(reference.Version, dependency.Version, StringComparison.Ordinal))
            {
                return $"Lock file reference '{reference.Id}' has a conflicting version.";
            }

            var validationError = ValidateReachablePack(
                dependency,
                resolvedPacksByKey,
                reachablePacks,
                visitingPacks
            );
            if (validationError is not null)
            {
                return validationError;
            }
        }

        visitingPacks.Remove(key);
        reachablePacks.Add(key);
        return null;
    }

    private static string? ValidateUniqueOwnership(ProjectLockFile lockFile)
    {
        var owners = new Dictionary<string, (string Owner, bool AllowsSharedSection)>(
            StringComparer.Ordinal
        );
        foreach (var instance in lockFile.Instances)
        {
            var error = AddOwnedTargets(
                instance.ManagedFiles,
                $"pack instance '{instance.Id}/{instance.Name}'",
                owners
            );
            if (error is not null)
            {
                return error;
            }
        }

        foreach (var pack in lockFile.Packs)
        {
            var error = AddOwnedTargets(
                pack.ManagedFiles,
                $"resolved pack '{pack.Id}@{pack.Version}'",
                owners
            );
            if (error is not null)
            {
                return error;
            }
        }

        foreach (var (name, link) in lockFile.Links)
        {
            var error = AddOwnedTargets(
                link.Files.Select(file => new ProjectLockFile.ManagedFile
                {
                    DeclaredTargetPath = file.DeclaredTargetPath,
                    Sha256 = file.Sha256,
                    TargetPath = file.TargetPath,
                }),
                $"link '{name}'",
                owners
            );
            if (error is not null)
            {
                return error;
            }
        }

        return null;
    }

    private static string? AddOwnedTargets(
        IEnumerable<ProjectLockFile.ManagedFile> managedFiles,
        string owner,
        Dictionary<string, (string Owner, bool AllowsSharedSection)> owners
    )
    {
        foreach (var managedFile in managedFiles)
        {
            var allowsSharedSection = managedFile.Strategy is { Type: "merge", Method: "section" };
            if (
                !owners.TryAdd(managedFile.TargetPath, (owner, allowsSharedSection))
                && (!allowsSharedSection || !owners[managedFile.TargetPath].AllowsSharedSection)
            )
            {
                return $"Lock file target '{managedFile.TargetPath}' has multiple owners: {owners[managedFile.TargetPath].Owner} and {owner}.";
            }
        }

        return null;
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
}
