using System.IO.Abstractions;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Lunapack.Cli;

internal sealed class ProjectStateStore : IProjectStateStore
{
    public const string ConfigurationFileName = "lunapack.yml";

    public const string LockFileName = "lunapack-lock.yml";

    private static readonly IDeserializer _deserializer = new StaticDeserializerBuilder(
        new LunapackYamlContext()
    )
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .WithTypeConverter(new ProjectConfigurationSourceYamlTypeConverter())
        .Build();

    private static readonly ISerializer _serializer = new StaticSerializerBuilder(
        new LunapackYamlContext()
    )
        .ConfigureDefaultValuesHandling(DefaultValuesHandling.OmitNull)
        .DisableAliases()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .Build();

    private readonly IFileSystem _fileSystem;

    public ProjectStateStore(IFileSystem fileSystem)
    {
        this._fileSystem = fileSystem;
    }

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

        var normalizedState = NormalizeState(
            new ProjectState { Configuration = loadedConfiguration, LockFile = loadedLockFile }
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

        return ManifestOperationResult<ProjectState>.Success(normalizedState);
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
        var normalizedState = NormalizeState(state);
        if (
            !await IsValidAsync(normalizedState.Configuration, ManifestModelValidator.Validate)
            || !await IsValidAsync(normalizedState.LockFile, ManifestModelValidator.Validate)
        )
        {
            return ManifestOperationResult<bool>.Failure(
                "Refusing to write project state that does not match the schemas."
            );
        }

        var validationError = ValidateState(
            normalizedState.Configuration,
            normalizedState.LockFile,
            allowUnconfiguredLockSources
        );
        if (validationError is not null)
        {
            return ManifestOperationResult<bool>.Failure(validationError);
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
            RestoreSnapshots(snapshots);
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

        return ValidateRequestedRoots(configuration, resolvedPacksById);
    }

    private static string? ValidateLinks(
        ProjectConfiguration configuration,
        ProjectLockFile lockFile,
        bool allowUnconfiguredLockSources
    )
    {
        foreach (var linkName in configuration.Links.Keys)
        {
            if (
                configuration.Packs.Any(pack =>
                    string.Equals(pack.Id, linkName, StringComparison.Ordinal)
                )
            )
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

            if (
                !allowUnconfiguredLockSources
                && !configuration.Sources.Any(source =>
                    string.Equals(source.Name, resolvedLink.SourceName, StringComparison.Ordinal)
                    && ConfiguredSourceIdentity.Create(source) == resolvedLink.SourceIdentity
                )
            )
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
    ) =>
        resolvedLink with
        {
            Files = [.. resolvedLink.Files.Select(NormalizeLinkFile)],
            GitSource = NormalizeGitSource(resolvedLink.GitSource),
            SourceIdentity = NormalizeSourceIdentity(resolvedLink.SourceIdentity)!,
        };

    private static ProjectLockFile.LinkFile NormalizeLinkFile(ProjectLockFile.LinkFile file) =>
        file with
        {
            DeclaredTargetPath = ProjectPath.Normalize(file.DeclaredTargetPath),
            SourcePath = ProjectPath.Normalize(file.SourcePath),
            TargetPath = ProjectPath.Normalize(file.TargetPath),
        };

    private static ProjectConfiguration.RequestedPack NormalizeRequestedPack(
        ProjectConfiguration.RequestedPack pack
    ) => pack with { Destination = ProjectPath.NormalizeOptional(pack.Destination) };

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
            PackPath = ProjectPath.Normalize(pack.PackPath),
            SourceIdentity = NormalizeSourceIdentity(pack.SourceIdentity),
            SourcePath = ProjectPath.NormalizeOptional(pack.SourcePath),
        };

    private static ConfiguredSourceIdentity? NormalizeSourceIdentity(
        ConfiguredSourceIdentity? source
    ) => source is null ? null : source with { Path = ProjectPath.NormalizeOptional(source.Path) };

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

            if (
                !allowUnconfiguredLockSources
                && !MatchesConfiguredSourceIdentity(configuration.Sources, resolvedPack)
            )
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

    private static string? ValidateRequestedRoots(
        ProjectConfiguration configuration,
        Dictionary<string, ProjectLockFile.ResolvedPack> resolvedPacksById
    )
    {
        var requestedRoots = new HashSet<string>(StringComparer.Ordinal);
        var reachablePacks = new HashSet<string>(StringComparer.Ordinal);
        var visitingPacks = new HashSet<string>(StringComparer.Ordinal);
        foreach (var requestedPack in configuration.Packs)
        {
            if (!requestedRoots.Add(requestedPack.Id))
            {
                return $"Project configuration contains duplicate requested pack '{requestedPack.Id}'.";
            }

            if (!resolvedPacksById.TryGetValue(requestedPack.Id, out var resolvedPack))
            {
                return $"Lock file does not contain requested pack '{requestedPack.Id}'.";
            }

            if (
                requestedPack.Version is not null
                && !string.Equals(
                    requestedPack.Version,
                    resolvedPack.Version,
                    StringComparison.Ordinal
                )
            )
            {
                return $"Lock file version for '{requestedPack.Id}' does not match the requested version.";
            }

            if (
                !string.Equals(
                    requestedPack.Destination,
                    resolvedPack.Destination,
                    StringComparison.Ordinal
                )
            )
            {
                return $"Lock file destination for '{requestedPack.Id}' does not match the requested destination.";
            }

            var validationError = ValidateReachablePack(
                resolvedPack,
                resolvedPacksById,
                reachablePacks,
                visitingPacks
            );
            if (validationError is not null)
            {
                return validationError;
            }
        }

        return reachablePacks.Count == resolvedPacksById.Count
            ? null
            : "Lock file contains packs that are unreachable from requested packs.";
    }

    private static string? ValidateReachablePack(
        ProjectLockFile.ResolvedPack pack,
        Dictionary<string, ProjectLockFile.ResolvedPack> resolvedPacksById,
        ISet<string> reachablePacks,
        ISet<string> visitingPacks
    )
    {
        if (reachablePacks.Contains(pack.Id))
        {
            return null;
        }

        if (!visitingPacks.Add(pack.Id))
        {
            return $"Lock file contains a dependency cycle at '{pack.Id}@{pack.Version}'.";
        }

        foreach (var reference in pack.Packs)
        {
            if (!resolvedPacksById.TryGetValue(reference.Id, out var dependency))
            {
                return $"Lock file reference '{reference.Id}@{reference.Version}' is unavailable.";
            }

            if (!string.Equals(reference.Version, dependency.Version, StringComparison.Ordinal))
            {
                return $"Lock file reference '{reference.Id}' has a conflicting version.";
            }

            var validationError = ValidateReachablePack(
                dependency,
                resolvedPacksById,
                reachablePacks,
                visitingPacks
            );
            if (validationError is not null)
            {
                return validationError;
            }
        }

        visitingPacks.Remove(pack.Id);
        reachablePacks.Add(pack.Id);
        return null;
    }

    private void RestoreSnapshots(IReadOnlyList<DocumentSnapshot> snapshots)
    {
        foreach (var snapshot in snapshots)
        {
            if (snapshot.Content is null)
            {
                if (_fileSystem.File.Exists(snapshot.Path))
                {
                    _fileSystem.File.Delete(snapshot.Path);
                }

                continue;
            }

            _fileSystem.File.WriteAllText(snapshot.Path, snapshot.Content);
        }
    }

    private sealed record DocumentSnapshot(string Path, string? Content);
}
