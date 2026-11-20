using System.IO.Abstractions;

namespace Lunapack.Cli.Application.Paths;

internal static class ProjectIdentityMap
{
    public static bool ValidateForLoad(IFileSystem fileSystem, IEnumerable<string> projectPaths)
    {
        var identities = new HashSet<string>(StringComparer.Ordinal);
        foreach (var projectPath in projectPaths)
        {
            if (
                !TryResolveIdentity(
                    fileSystem,
                    ProjectPath.Normalize(projectPath),
                    out var identity
                ) || !identities.Add(identity)
            )
            {
                return false;
            }
        }

        return true;
    }

    public static bool ValidateForSave(IFileSystem fileSystem, IEnumerable<string> projectPaths)
    {
        var identities = new HashSet<string>(StringComparer.Ordinal);
        foreach (var projectPath in projectPaths)
        {
            if (
                !TryResolveIdentity(fileSystem, projectPath, out var identity)
                || !identities.Add(identity)
                || (
                    fileSystem.Directory.Exists(projectPath)
                    && !string.Equals(projectPath, identity, StringComparison.Ordinal)
                )
            )
            {
                return false;
            }
        }

        return true;
    }

    public static Dictionary<string, TValue> Normalize<TValue>(
        IFileSystem fileSystem,
        IReadOnlyDictionary<string, TValue> values
    )
    {
        var normalized = new Dictionary<string, TValue>(StringComparer.Ordinal);
        foreach (var (projectPath, value) in values)
        {
            var canonicalPath = CanonicalProjectPath.Resolve(fileSystem, projectPath);
            normalized[canonicalPath.Value ?? projectPath] = value;
        }

        return normalized;
    }

    public static Dictionary<string, TValue> NormalizeSeparators<TValue>(
        IReadOnlyDictionary<string, TValue> values
    ) =>
        values.ToDictionary(
            entry => ProjectPath.Normalize(entry.Key),
            entry => entry.Value,
            StringComparer.Ordinal
        );

    private static bool TryResolveIdentity(
        IFileSystem fileSystem,
        string projectPath,
        out string identity
    )
    {
        try
        {
            var fullPath = fileSystem.Path.GetFullPath(projectPath);
            var normalizedPath = ProjectPath.Normalize(
                fileSystem.Path.TrimEndingDirectorySeparator(fullPath)
            );
            if (!string.Equals(projectPath, normalizedPath, StringComparison.Ordinal))
            {
                identity = string.Empty;
                return false;
            }

            if (!fileSystem.Directory.Exists(fullPath))
            {
                identity = fullPath;
                return true;
            }

            var canonicalPath = CanonicalProjectPath.Resolve(fileSystem, fullPath);
            identity = canonicalPath.Value ?? string.Empty;
            return canonicalPath.IsSuccess;
        }
        catch (Exception exception)
            when (exception is ArgumentException or IOException or NotSupportedException)
        {
            identity = string.Empty;
            return false;
        }
    }
}
