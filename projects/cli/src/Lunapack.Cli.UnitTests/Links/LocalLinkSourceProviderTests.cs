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
        fileSystem.AddFile(@"C:\project\source\escaped\secret.txt", "secret");
        fileSystem.File.SetAttributes(
            @"C:\project\source\escaped",
            FileAttributes.Directory | FileAttributes.ReparsePoint
        );
        var provider = new LocalLinkSourceProvider(fileSystem);

        var result = await provider.ListAsync(
            @"C:\project",
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
        fileSystem.AddFile(@"C:\project\source\nested\secret.txt", "secret");
        var listing = new LinkSourceListing(
            ConfiguredSourceIdentity.CreateLocal("source"),
            GitSource: null,
            @"C:\project\source",
            ["nested/secret.txt"]
        );
        fileSystem.File.SetAttributes(
            @"C:\project\source\nested",
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
