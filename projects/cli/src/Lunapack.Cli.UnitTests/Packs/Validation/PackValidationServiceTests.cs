using Lunapack.Cli.Application.CommandExecution;
using Lunapack.Cli.Catalog;
using Lunapack.Cli.Packs.Validation;
using Lunapack.Cli.Sources.Git;

namespace Lunapack.Cli.UnitTests.Packs.Validation;

public sealed class PackValidationServiceTests
{
    [Test]
    public async Task ValidateCommand_WhenExactGitPackIsDraft_ReturnsSuccess()
    {
        using var workspace = new TestWorkspace(gitProcessRunner: new DraftPackGitProcessRunner());
        await workspace.Application.RunAsync(["init"], workspace.Path);
        await workspace.Application.RunAsync(
            [
                "sources",
                "add",
                "github",
                "remote",
                "lunarisdigitalsolutions/lunapack",
                "--ref",
                "main",
                "--path",
                "packs",
            ],
            workspace.Path
        );

        var exitCode = await workspace.Application.RunAsync(
            ["validate", "draft-example@1.0.0"],
            workspace.Path
        );

        await Assert.That(exitCode).IsEqualTo(0);
    }

    [Test]
    public async Task ValidateAsync_WhenLatestPackSourceMissing_ReturnsItsIssues()
    {
        using var workspace = new TestWorkspace();
        var sourceDirectory = Path.Combine(workspace.Path, "source");
        CreatePack(sourceDirectory, "example-v1", "1.0.0", createSourceFile: true);
        CreatePack(sourceDirectory, "example-v2", "2.0.0", createSourceFile: false);
        await workspace.Application.RunAsync(["init"], workspace.Path);
        await workspace.Application.RunAsync(
            ["sources", "add", "local", "local", "source"],
            workspace.Path
        );
        var validationService = new PackValidationService(
            workspace.FileSystem,
            workspace.StateStore,
            new LocalPackDiscovery(workspace.FileSystem, TestConsole.Create()),
            new PackCatalog(workspace.FileSystem, TestConsole.Create())
        );

        var result = await validationService.ValidateAsync(workspace.Path, "example", null);

        await Assert.That(result.IsSuccess).IsTrue();
        var validation = result.RequireValue();
        await Assert.That(validation.Manifest.RequireNotNull().Version).IsEqualTo("2.0.0");
        await Assert.That(validation.IsValid).IsFalse();
        await Assert
            .That(validation.Issues)
            .Contains("Pack 'example' source file 'templates/content.txt' is unavailable.");
    }

    [Test]
    public async Task ValidateCommand_WhenPackSourceMissing_ReturnsFailure()
    {
        using var workspace = new TestWorkspace();
        var sourceDirectory = Path.Combine(workspace.Path, "source");
        CreatePack(sourceDirectory, "example", "1.0.0", createSourceFile: false);
        await workspace.Application.RunAsync(["init"], workspace.Path);
        await workspace.Application.RunAsync(
            ["sources", "add", "local", "local", "source"],
            workspace.Path
        );

        var exitCode = await workspace.Application.RunAsync(
            ["validate", "example"],
            workspace.Path
        );

        await Assert.That(exitCode).IsEqualTo(1);
    }

    private static void CreatePack(
        string sourceDirectory,
        string directoryName,
        string version,
        bool createSourceFile
    )
    {
        var packDirectory = Path.Combine(sourceDirectory, directoryName);
        var templatesDirectory = Path.Combine(packDirectory, "templates");
        Directory.CreateDirectory(templatesDirectory);
        File.WriteAllText(
            Path.Combine(packDirectory, "pack.yml"),
            $"id: example\nversion: {version}\nlicense: MIT\nauthor: Lunaris Digital Solutions <info@lunaris.digital>\nmanagedFiles:\n  - source: templates/content.txt\n    target: content.txt\n"
        );
        if (createSourceFile)
        {
            File.WriteAllText(Path.Combine(templatesDirectory, "content.txt"), "content");
        }
    }

    private sealed class DraftPackGitProcessRunner : IGitProcessRunner
    {
        private const string Commit = "1111111111111111111111111111111111111111";

        public Task<ManifestOperationResult<GitProcessOutput>> RunAsync(
            IReadOnlyList<string> arguments,
            TimeSpan timeout,
            CancellationToken cancellationToken
        )
        {
            var output =
                string.Equals(arguments[0], "ls-remote", StringComparison.Ordinal)
                    ? $"{Commit}\trefs/heads/main\n"
                : arguments.Contains("ls-tree", StringComparer.Ordinal)
                    ? "packs/draft-example/pack.yml\n"
                : arguments.Contains("show", StringComparer.Ordinal)
                    ? "id: draft-example\nversion: 1.0.0\ndraft: true\nauthor: Example\nlicense: MIT\n"
                : string.Empty;
            return Task.FromResult(
                ManifestOperationResult<GitProcessOutput>.Success(
                    new GitProcessOutput(output, string.Empty)
                )
            );
        }
    }
}
