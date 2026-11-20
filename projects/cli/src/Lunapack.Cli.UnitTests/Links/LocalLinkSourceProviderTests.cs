using System.IO.Abstractions.TestingHelpers;
using Lunapack.Cli.Links;
using Lunapack.Cli.Project;
using Lunapack.Cli.Sources;

namespace Lunapack.Cli.UnitTests.Links;

public sealed class LocalLinkSourceProviderTests
{
    [Test]
    public async Task ListAsync_WhenDescendantIsReparsePoint_RejectsSource()
    {
        var fileSystem = new MockFileSystem();
        var projectDirectory = fileSystem.Path.GetFullPath("project");
        var escapedDirectory = fileSystem.Path.Combine(projectDirectory, "source", "escaped");
        fileSystem.AddFile(fileSystem.Path.Combine(escapedDirectory, "secret.txt"), "secret");
        fileSystem.Directory.CreateDirectory(escapedDirectory);
        fileSystem.File.SetAttributes(
            escapedDirectory,
            FileAttributes.Directory | FileAttributes.ReparsePoint
        );
        var provider = new LocalLinkSourceProvider(fileSystem);

        var result = await provider.ListAsync(
            projectDirectory,
            CreateSource(),
            CreateLink(),
            lockedIdentity: null,
            cancellationToken: CancellationToken.None
        );

        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That(result.Error).Contains("symbolic link or reparse point");
    }

    [Test]
    public async Task MaterializeAsync_WhenParentBecomesReparsePoint_RejectsPath()
    {
        var fileSystem = new MockFileSystem();
        var projectDirectory = fileSystem.Path.GetFullPath("project");
        var sourceDirectory = fileSystem.Path.Combine(projectDirectory, "source");
        var nestedDirectory = fileSystem.Path.Combine(sourceDirectory, "nested");
        fileSystem.AddFile(fileSystem.Path.Combine(nestedDirectory, "secret.txt"), "secret");
        fileSystem.Directory.CreateDirectory(nestedDirectory);
        var listing = new LinkSourceListing(
            ConfiguredSourceIdentity.CreateLocal("source"),
            GitSource: null,
            sourceDirectory,
            ["nested/secret.txt"]
        );
        fileSystem.File.SetAttributes(
            nestedDirectory,
            FileAttributes.Directory | FileAttributes.ReparsePoint
        );
        using var workspace = LinkOperationWorkspace.Create(fileSystem);
        var provider = new LocalLinkSourceProvider(fileSystem);

        var result = await provider.MaterializeAsync(
            listing,
            ["nested/secret.txt"],
            workspace,
            cancellationToken: CancellationToken.None
        );

        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That(result.Error).Contains("symbolic link or reparse point");
    }

    private static ProjectConfiguration.LocalSource CreateSource() =>
        new() { Name = "local", Path = "source" };

    private static ProjectConfiguration.Link CreateLink() =>
        new() { Source = "local", Includes = ["**/*"] };
}
