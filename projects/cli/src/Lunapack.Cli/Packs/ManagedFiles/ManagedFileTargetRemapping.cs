using System.IO.Abstractions;
using Lunapack.Cli.Application.CommandExecution;
using Lunapack.Cli.Application.Paths;
using Lunapack.Cli.Project;

namespace Lunapack.Cli.Packs.ManagedFiles;

internal sealed class ManagedFileTargetRemapping
{
    public const string IgnoreTarget = "@ignore";

    private static readonly StringComparer _pathComparer = StringComparer.Ordinal;
    private readonly IReadOnlyDictionary<string, string> _directories;
    private readonly IReadOnlyDictionary<string, string> _files;

    private ManagedFileTargetRemapping(
        IReadOnlyDictionary<string, string> directories,
        IReadOnlyDictionary<string, string> files
    )
    {
        _directories = directories;
        _files = files;
    }

    public bool HasMappings => _directories.Count > 0 || _files.Count > 0;

    public ProjectConfiguration.Remapping MergeInto(ProjectConfiguration.Remapping? remapping)
    {
        var directories = new Dictionary<string, string>(
            remapping?.Directories ?? [],
            _pathComparer
        );
        var files = new Dictionary<string, string>(remapping?.Files ?? [], _pathComparer);
        foreach (var (source, target) in _directories)
        {
            directories[source] = target;
        }

        foreach (var (source, target) in _files)
        {
            files[source] = target;
        }

        return new ProjectConfiguration.Remapping { Directories = directories, Files = files };
    }

    public static ManagedFileTargetRemapping FromConfiguration(
        ProjectConfiguration.Remapping? remapping
    ) =>
        remapping is null
            ? new ManagedFileTargetRemapping(
                new Dictionary<string, string>(_pathComparer),
                new Dictionary<string, string>(_pathComparer)
            )
            : new ManagedFileTargetRemapping(
                NormalizeMappings(remapping.Directories),
                NormalizeMappings(remapping.Files)
            );

    public static ManifestOperationResult<ManagedFileTargetRemapping> Create(
        IFileSystem fileSystem,
        string projectDirectory,
        IEnumerable<string> directoryMappings,
        IEnumerable<string> fileMappings
    )
    {
        var directories = ParseMappings(
            fileSystem,
            projectDirectory,
            directoryMappings,
            "directory"
        );
        if (directories.Value is not { } parsedDirectories)
        {
            return ManifestOperationResult<ManagedFileTargetRemapping>.Failure(
                directories.Error ?? "Invalid directory remapping."
            );
        }

        var files = ParseMappings(fileSystem, projectDirectory, fileMappings, "file");
        return files.Value is { } parsedFiles
            ? ManifestOperationResult<ManagedFileTargetRemapping>.Success(
                new ManagedFileTargetRemapping(parsedDirectories, parsedFiles)
            )
            : ManifestOperationResult<ManagedFileTargetRemapping>.Failure(
                files.Error ?? "Invalid file remapping."
            );
    }

    public string Resolve(
        string declaredTarget,
        params ManagedFileTargetRemapping?[] fallbackRemappings
    )
    {
        var normalizedTarget = NormalizeMappingPath(declaredTarget);
        var target = TryResolve(normalizedTarget);
        if (target is not null)
        {
            return target;
        }

        foreach (var fallbackRemapping in fallbackRemappings)
        {
            target = fallbackRemapping?.TryResolve(normalizedTarget);
            if (target is not null)
            {
                return target;
            }
        }

        return normalizedTarget;
    }

    public string? TryResolve(string declaredTarget) =>
        TryResolveNormalized(NormalizeMappingPath(declaredTarget));

    private string? TryResolveNormalized(string declaredTarget) =>
        TryResolveFile(declaredTarget) ?? TryResolveDirectory(declaredTarget);

    private static ManifestOperationResult<Dictionary<string, string>> ParseMappings(
        IFileSystem fileSystem,
        string projectDirectory,
        IEnumerable<string> mappings,
        string mappingType
    )
    {
        var parsedMappings = new Dictionary<string, string>(_pathComparer);
        foreach (var mapping in mappings)
        {
            var separatorIndex = mapping.IndexOf('=', StringComparison.Ordinal);
            if (separatorIndex <= 0 || separatorIndex == mapping.Length - 1)
            {
                return ManifestOperationResult<Dictionary<string, string>>.Failure(
                    $"Invalid {mappingType} remapping '{mapping}'. Expected <source>=<target>."
                );
            }

            var source = ProjectPath.NormalizeProjectRelativePath(
                fileSystem,
                projectDirectory,
                mapping[..separatorIndex]
            );
            if (source.Value is not { } normalizedSource)
            {
                return ManifestOperationResult<Dictionary<string, string>>.Failure(
                    $"Invalid {mappingType} remapping source '{mapping[..separatorIndex]}': {source.Error}"
                );
            }

            var target = ProjectPath.NormalizeProjectRelativePath(
                fileSystem,
                projectDirectory,
                mapping[(separatorIndex + 1)..]
            );
            if (target.Value is not { } normalizedTarget)
            {
                return ManifestOperationResult<Dictionary<string, string>>.Failure(
                    $"Invalid {mappingType} remapping target '{mapping[(separatorIndex + 1)..]}': {target.Error}"
                );
            }

            if (!parsedMappings.TryAdd(normalizedSource, normalizedTarget))
            {
                return ManifestOperationResult<Dictionary<string, string>>.Failure(
                    $"{mappingType} remapping source '{normalizedSource}' was supplied more than once."
                );
            }
        }

        return ManifestOperationResult<Dictionary<string, string>>.Success(parsedMappings);
    }

    private static Dictionary<string, string> NormalizeMappings(
        IReadOnlyDictionary<string, string> mappings
    )
    {
        var normalizedMappings = new Dictionary<string, string>(_pathComparer);
        foreach (var (source, target) in mappings)
        {
            normalizedMappings.Add(NormalizeMappingPath(source), NormalizeMappingPath(target));
        }

        return normalizedMappings;
    }

    private static string NormalizeMappingPath(string path) =>
        ProjectPath.Normalize(path).TrimEnd('/');

    private string? TryResolveFile(string declaredTarget) =>
        _files.TryGetValue(declaredTarget, out var fileTarget) ? fileTarget : null;

    private string? TryResolveDirectory(string declaredTarget)
    {
        foreach (
            var (sourceDirectory, targetDirectory) in _directories.OrderByDescending(mapping =>
                mapping.Key.Length
            )
        )
        {
            if (string.Equals(sourceDirectory, declaredTarget, StringComparison.Ordinal))
            {
                return targetDirectory;
            }

            var directoryPrefix = $"{sourceDirectory}/";
            if (declaredTarget.StartsWith(directoryPrefix, StringComparison.Ordinal))
            {
                return string.Equals(targetDirectory, IgnoreTarget, StringComparison.Ordinal)
                    ? IgnoreTarget
                    : $"{targetDirectory}/{declaredTarget[directoryPrefix.Length..]}";
            }
        }

        return null;
    }
}
