using System.IO.Abstractions;
using Lunapack.Cli.Application.CommandExecution;
using Lunapack.Cli.Application.Paths;
using Lunapack.Cli.Project;

namespace Lunapack.Cli.Sources;

internal sealed record ConfiguredSourceIdentity
{
    public string? Path { get; set; }

    public string? Ref { get; set; }

    public required string Type { get; set; }

    public string? Url { get; set; }

    public static ConfiguredSourceIdentity Create(ProjectConfiguration.Source source) =>
        source switch
        {
            ProjectConfiguration.LocalSource localSource => CreateLocal(localSource.Path),
            ProjectConfiguration.GitSource gitSource => CreateGit(
                gitSource.Url,
                gitSource.Ref,
                gitSource.Path
            ),
            _ => throw new ArgumentException("Unsupported configured source type.", nameof(source)),
        };

    public static ConfiguredSourceIdentity CreateLocal(string path) =>
        new() { Type = "local", Path = ProjectPath.Normalize(path) };

    public static ConfiguredSourceIdentity CreateGit(string url, string? reference, string? path) =>
        new()
        {
            Type = "git",
            Url = url.Trim().TrimEnd('/'),
            Ref = reference?.Trim(),
            Path = ProjectPath.NormalizeOptional(path)?.Trim('/'),
        };

    public static ManifestOperationResult<ConfiguredSourceIdentity> CreateForTrust(
        IFileSystem fileSystem,
        string projectDirectory,
        ProjectConfiguration.Source source
    )
    {
        if (source is not ProjectConfiguration.LocalSource localSource)
        {
            return ManifestOperationResult<ConfiguredSourceIdentity>.Success(Create(source));
        }

        var sourceDirectory = fileSystem.Path.GetFullPath(localSource.Path, projectDirectory);
        var canonicalPath = CanonicalProjectPath.Resolve(fileSystem, sourceDirectory);
        return canonicalPath.Value is { } path
            ? ManifestOperationResult<ConfiguredSourceIdentity>.Success(CreateLocal(path))
            : ManifestOperationResult<ConfiguredSourceIdentity>.Failure(
                canonicalPath.Error ?? $"Unable to resolve source '{source.Name}'."
            );
    }
}
