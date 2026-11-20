using System.CommandLine;
using System.IO.Abstractions;
using System.Text;
using Lunapack.Cli.Application;
using Lunapack.Cli.Application.Paths;
using Lunapack.Cli.Project;

namespace Lunapack.Cli.Sbom;

internal sealed class SbomCommandHandler(
    IFileSystem fileSystem,
    ProjectStateStore projectStateStore,
    WorkspaceDirectoryResolver workspaceDirectoryResolver,
    TextWriter output,
    TextWriter error,
    TimeProvider timeProvider
)
{
    public Command CreateCommand(string projectDirectory, Option<string?> workspaceOption)
    {
        var targetOption = new Option<string>("--target")
        {
            Description =
                $"Target format: {string.Join(", ", SbomTarget.Supported.Select(target => target.Name))}.",
            Required = true,
        };
        var outputOption = new Option<string?>("--output")
        {
            Description = "Project-relative JSON output file; omit to write to stdout.",
        };
        var command = new Command(
            "sbom",
            "Export dependency inventory from the project lock file. The inventory is not a signature or attestation."
        )
        {
            targetOption,
            outputOption,
        };
        command.SetAction(async parseResult =>
            await ExportAsync(
                workspaceDirectoryResolver.Resolve(
                    projectDirectory,
                    parseResult.GetValue(workspaceOption)
                ),
                parseResult.GetValue(targetOption),
                parseResult.GetValue(outputOption)
            )
        );

        return command;
    }

    private async Task<int> ExportAsync(
        string projectDirectory,
        string? targetName,
        string? outputPath
    )
    {
        var target = SbomTarget.Parse(targetName);
        if (target is null)
        {
            return Fail(
                $"Unsupported SBOM target '{targetName}'. Supported targets: {string.Join(", ", SbomTarget.Supported.Select(candidate => candidate.Name))}."
            );
        }

        var stateResult = await projectStateStore.LoadAsync(projectDirectory);
        if (stateResult.Value is not { } state)
        {
            return Fail(stateResult.Error ?? "Unable to load project state.");
        }

        SbomInventory inventory;
        try
        {
            var pathComparison = GetProjectPathComparison(projectDirectory);
            inventory = SbomInventoryBuilder.Build(
                state.LockFile,
                path => NormalizeInventoryPath(projectDirectory, path),
                pathComparison
            );
        }
        catch (InvalidDataException exception)
        {
            return Fail($"Invalid SBOM lock evidence: {exception.Message}");
        }

        var document = SbomDocumentSerializer.Serialize(
            inventory,
            target,
            timeProvider.GetUtcNow()
        );
        if (outputPath is null)
        {
            await output.WriteLineAsync(document);
            return 0;
        }

        var normalizedPath = ProjectPath.NormalizeProjectRelativePath(
            fileSystem,
            projectDirectory,
            outputPath
        );
        if (normalizedPath.Value is not { } relativePath || relativePath is "" or ".")
        {
            return Fail(
                $"Invalid SBOM output path '{outputPath}': {normalizedPath.Error ?? "path must name a file"}"
            );
        }

        var collision = FindProtectedPathCollision(projectDirectory, relativePath, state.LockFile);
        if (collision is not null)
        {
            return Fail(
                $"SBOM output path '{relativePath}' conflicts with protected project path '{collision}'."
            );
        }

        var fullPath = fileSystem.Path.GetFullPath(relativePath, projectDirectory);
        try
        {
            ProjectMutationPathSecurity.EnsureNoAliases(fileSystem, projectDirectory, fullPath);
            ProjectMutationPathSecurity.ReplaceFile(
                fileSystem,
                projectDirectory,
                fullPath,
                Encoding.UTF8.GetBytes($"{document}{Environment.NewLine}")
            );
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return Fail($"Unable to write SBOM to '{relativePath}': {exception.Message}");
        }

        return 0;
    }

    private string? FindProtectedPathCollision(
        string projectDirectory,
        string outputPath,
        ProjectLockFile lockFile
    )
    {
        var protectedPaths = new[]
        {
            ProjectStateStore.ConfigurationFileName,
            ProjectStateStore.LockFileName,
        }.Concat(
            lockFile
                .Packs.SelectMany(pack => pack.ManagedFiles)
                .Select(file => file.TargetPath)
                .Concat(
                    lockFile
                        .Links.Values.SelectMany(link => link.Files)
                        .Select(file => file.TargetPath)
                )
        );

        var outputKey = NormalizeComparisonPath(projectDirectory, outputPath);
        var comparison = GetProjectPathComparison(projectDirectory);
        return protectedPaths.FirstOrDefault(path =>
            string.Equals(NormalizeComparisonPath(projectDirectory, path), outputKey, comparison)
        );
    }

    private string NormalizeInventoryPath(string projectDirectory, string path)
    {
        var normalized = ProjectPath.NormalizeProjectRelativePath(
            fileSystem,
            projectDirectory,
            path
        );
        return normalized.Value
            ?? throw new InvalidDataException(
                $"Managed target '{path}' is invalid: {normalized.Error ?? "path must be project-relative"}"
            );
    }

    private string NormalizeComparisonPath(string projectDirectory, string path) =>
        NormalizeInventoryPath(projectDirectory, path).Normalize(NormalizationForm.FormC);

    private StringComparison GetProjectPathComparison(string projectDirectory)
    {
        var alternateConfigurationPath = fileSystem.Path.Combine(
            projectDirectory,
            ProjectStateStore.ConfigurationFileName.ToUpperInvariant()
        );
        return fileSystem.File.Exists(alternateConfigurationPath)
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
    }

    private int Fail(string message)
    {
        error.WriteLine($"error: {message}");
        return 1;
    }
}
