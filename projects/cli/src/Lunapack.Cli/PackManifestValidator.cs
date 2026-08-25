using System.Text.Json;
using Microsoft.Extensions.FileSystemGlobbing;

namespace Lunapack.Cli;

internal static class PackManifestValidator
{
    public static Task<IReadOnlyList<string>> ValidateAsync(
        PackManifest manifest,
        IReadOnlyCollection<string> sourceFiles
    )
    {
        var issues = ManifestModelValidator.Validate(manifest).ToList();

        var normalizedSourceFiles = sourceFiles
            .Select(NormalizePath)
            .ToHashSet(StringComparer.Ordinal);
        foreach (var managedFile in manifest.ManagedFiles)
        {
            ValidateSelector(manifest.Id, managedFile, normalizedSourceFiles, issues);
        }

        return Task.FromResult<IReadOnlyList<string>>(issues);
    }

    private static void ValidateSelector(
        string packId,
        PackManifest.PackManagedFile managedFile,
        HashSet<string> sourceFiles,
        List<string> issues
    )
    {
        if (managedFile.Source is { } source)
        {
            ValidateSourceFile(packId, source, sourceFiles, issues);
            return;
        }

        if (managedFile.Directory is { } directory)
        {
            ValidateSourceDirectory(packId, directory, sourceFiles, issues);
            return;
        }

        if (managedFile.Glob is { } glob)
        {
            ValidateSourceGlob(packId, glob, sourceFiles, issues);
        }
    }

    private static void ValidateSourceFile(
        string packId,
        string source,
        HashSet<string> sourceFiles,
        List<string> issues
    )
    {
        if (!sourceFiles.Contains(NormalizePath(source)))
        {
            issues.Add($"Pack '{packId}' source file '{source}' is unavailable.");
        }
    }

    private static void ValidateSourceDirectory(
        string packId,
        string directory,
        HashSet<string> sourceFiles,
        List<string> issues
    )
    {
        var normalizedDirectory = NormalizePath(directory).TrimEnd('/') + "/";
        if (
            !sourceFiles.Any(path => path.StartsWith(normalizedDirectory, StringComparison.Ordinal))
        )
        {
            issues.Add($"Pack '{packId}' source directory '{directory}' contains no files.");
        }
    }

    private static void ValidateSourceGlob(
        string packId,
        string glob,
        HashSet<string> sourceFiles,
        List<string> issues
    )
    {
        var matcher = new Matcher(StringComparison.Ordinal);
        matcher.AddInclude(NormalizePath(glob));
        if (!matcher.Match(sourceFiles).HasMatches)
        {
            issues.Add($"Pack '{packId}' glob '{glob}' matches no files.");
        }
    }

    private static string NormalizePath(string path) => ProjectPath.Normalize(path);
}
