using Lunapack.Cli.Application.Paths;
using Lunapack.Cli.Packs.ManagedFiles;
using Microsoft.Extensions.FileSystemGlobbing;

namespace Lunapack.Cli.Packs.Manifest;

internal static class PackManifestValidator
{
    public static Task<IReadOnlyList<string>> ValidateAsync(
        PackManifest manifest,
        IReadOnlyCollection<string> sourceFiles
    )
    {
        var issues = ManifestModelValidator.Validate(manifest).ToList();

        var normalizedSourceFiles = sourceFiles
            .Select(ProjectPath.Normalize)
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
        var created = PackManagedFileSelector.Create(managedFile);
        if (created.Value is not { } selector || selector.IsExternal)
        {
            return;
        }

        if (selector.Kind == PackManagedFileSelectorKind.File)
        {
            ValidateSourceFile(packId, selector.Value, sourceFiles, issues);
            return;
        }

        if (selector.Kind == PackManagedFileSelectorKind.Directory)
        {
            ValidateSourceDirectory(packId, selector.Value, sourceFiles, issues);
            return;
        }

        ValidateSourceGlob(packId, selector.Value, sourceFiles, issues);
    }

    private static void ValidateSourceFile(
        string packId,
        string source,
        HashSet<string> sourceFiles,
        List<string> issues
    )
    {
        if (!sourceFiles.Contains(ProjectPath.Normalize(source)))
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
        var normalizedDirectory = ProjectPath.Normalize(directory).TrimEnd('/') + "/";
        var hasSourceFiles = sourceFiles.Any(path =>
            path.StartsWith(normalizedDirectory, StringComparison.Ordinal)
        );
        if (!hasSourceFiles)
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
        matcher.AddInclude(ProjectPath.Normalize(glob));
        if (!matcher.Match(sourceFiles).HasMatches)
        {
            issues.Add($"Pack '{packId}' glob '{glob}' matches no files.");
        }
    }
}
