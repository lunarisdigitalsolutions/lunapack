namespace Lunapack.Cli.UnitTests;

public sealed class PackValidationServiceTests
{
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
            new LocalPackDiscovery(workspace.FileSystem, TestConsole.Create())
        );

        var result = await validationService.ValidateAsync(workspace.Path, "example", null);

        await Assert.That(result.IsSuccess).IsTrue();
        var validation = result.RequireValue();
        await Assert.That(validation.Manifest!.Version).IsEqualTo("2.0.0");
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
}
