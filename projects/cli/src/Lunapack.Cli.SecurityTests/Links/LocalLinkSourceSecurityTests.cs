using System.IO.Abstractions;
using Lunapack.Cli.Links;
using Lunapack.Cli.Project;

namespace Lunapack.Cli.SecurityTests.Links;

public sealed class LocalLinkSourceSecurityTests
{
    [Test]
    public async Task ListAsync_WhenDescendantDirectoryIsSymlink_RejectsOutsideSource()
    {
        var root = CreateTemporaryRoot();
        var projectDirectory = Path.Combine(root, "project");
        var sourceDirectory = Path.Combine(projectDirectory, "source");
        var outsideDirectory = Path.Combine(root, "outside");
        Directory.CreateDirectory(Path.Combine(sourceDirectory, "regular"));
        Directory.CreateDirectory(outsideDirectory);
        File.WriteAllText(Path.Combine(outsideDirectory, "secret.txt"), "secret");
        var linkPath = Path.Combine(sourceDirectory, "escaped");

        try
        {
            try
            {
                Directory.CreateSymbolicLink(linkPath, outsideDirectory);
            }
            catch (Exception exception)
                when (exception is IOException or UnauthorizedAccessException)
            {
                Skip.Test($"Directory symbolic links are unavailable: {exception.Message}");
            }

            var provider = new LocalLinkSourceProvider(new FileSystem());
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
        finally
        {
            DeleteTemporaryRoot(root);
        }
    }

    [Test]
    public async Task MaterializeAsync_WhenParentBecomesSymlink_RejectsBeforeCopy()
    {
        var root = CreateTemporaryRoot();
        var projectDirectory = Path.Combine(root, "project");
        var sourceDirectory = Path.Combine(projectDirectory, "source");
        var nestedDirectory = Path.Combine(sourceDirectory, "nested");
        var outsideDirectory = Path.Combine(root, "outside");
        Directory.CreateDirectory(nestedDirectory);
        Directory.CreateDirectory(outsideDirectory);
        File.WriteAllText(Path.Combine(nestedDirectory, "secret.txt"), "original");
        File.WriteAllText(Path.Combine(outsideDirectory, "secret.txt"), "outside");

        try
        {
            var fileSystem = new FileSystem();
            var provider = new LocalLinkSourceProvider(fileSystem);
            var listingResult = await provider.ListAsync(
                projectDirectory,
                CreateSource(),
                CreateLink(),
                lockedIdentity: null,
                cancellationToken: CancellationToken.None
            );
            await Assert.That(listingResult.IsSuccess).IsTrue();
            using var listing =
                listingResult.Value ?? throw new InvalidOperationException(listingResult.Error);
            Directory.Delete(nestedDirectory, recursive: true);
            try
            {
                Directory.CreateSymbolicLink(nestedDirectory, outsideDirectory);
            }
            catch (Exception exception)
                when (exception is IOException or UnauthorizedAccessException)
            {
                Skip.Test($"Directory symbolic links are unavailable: {exception.Message}");
            }

            using var workspace = LinkOperationWorkspace.Create(fileSystem);
            var result = await provider.MaterializeAsync(
                listing,
                ["nested/secret.txt"],
                workspace,
                cancellationToken: CancellationToken.None
            );

            await Assert.That(result.IsSuccess).IsFalse();
            await Assert.That(result.Error).Contains("symbolic link or reparse point");
            await Assert
                .That(
                    Directory
                        .EnumerateFiles(workspace.Directory, "*", SearchOption.AllDirectories)
                        .Any()
                )
                .IsFalse();
        }
        finally
        {
            DeleteTemporaryRoot(root);
        }
    }

    private static ProjectConfiguration.LocalSource CreateSource() =>
        new() { Name = "local", Path = "source" };

    private static ProjectConfiguration.Link CreateLink() =>
        new() { Source = "local", Includes = ["**/*"] };

    private static string CreateTemporaryRoot()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "lunapack-local-link-security",
            Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(root);
        return root;
    }

    private static void DeleteTemporaryRoot(string root)
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
