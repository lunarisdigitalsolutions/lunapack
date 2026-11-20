using System.IO.Abstractions;
using Spectre.Console;

namespace Lunapack.Cli.SecurityTests.Sbom;

[Property("FileSystem", "Real")]
public sealed class SbomOutputSecurityTests
{
    [Test]
    public async Task Sbom_WhenOutputCrossesDirectorySymbolicLink_PreservesOutsidePath()
    {
        var root = Path.Combine(Path.GetTempPath(), $"lunapack-security-{Guid.NewGuid():N}");
        var projectDirectory = Directory.CreateDirectory(Path.Combine(root, "project")).FullName;
        var outsideDirectory = Directory.CreateDirectory(Path.Combine(root, "outside")).FullName;
        var outsidePath = Path.Combine(outsideDirectory, "inventory.json");
        var alias = Path.Combine(projectDirectory, "linked");
        using var output = new StringWriter();
        using var error = new StringWriter();

        try
        {
            var application = CreateApplication();
            var initialized = await application.RunAsync(["init"], projectDirectory);
            await Assert.That(initialized).IsEqualTo(0);
            try
            {
                Directory.CreateSymbolicLink(alias, outsideDirectory);
            }
            catch (Exception exception)
                when (exception is IOException or UnauthorizedAccessException)
            {
                Skip.Test($"Symbolic links are unavailable: {exception.Message}");
            }

            var exitCode = await application.RunAsync(
                ["sbom", "--target", "cyclonedx-1.6", "--output", "linked/inventory.json"],
                projectDirectory,
                output,
                error
            );

            await Assert.That(exitCode).IsEqualTo(1);
            await Assert.That(output.ToString()).IsEmpty();
            await Assert.That(error.ToString()).Contains("link or reparse point");
            await Assert.That(File.Exists(outsidePath)).IsFalse();
        }
        finally
        {
            if (Directory.Exists(alias))
            {
                Directory.Delete(alias);
            }

            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static CliApplication CreateApplication() =>
        new(
            new FileSystem(),
            AnsiConsole.Create(
                new AnsiConsoleSettings
                {
                    Ansi = AnsiSupport.No,
                    ColorSystem = ColorSystemSupport.NoColors,
                    Out = new AnsiConsoleOutput(TextWriter.Null),
                }
            )
        );
}
