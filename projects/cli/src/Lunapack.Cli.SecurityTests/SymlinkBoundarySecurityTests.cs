using System.IO.Abstractions;
using Lunapack.Cli.Trust;

namespace Lunapack.Cli.SecurityTests;

public sealed class SymlinkBoundarySecurityTests
{
    [Test]
    public async Task ValidateExisting_WhenDirectoryIsSymbolicLink_RejectsBoundaryAlias()
    {
        var root = Path.Combine(Path.GetTempPath(), $"lunapack-security-{Guid.NewGuid():N}");
        var target = Directory.CreateDirectory(Path.Combine(root, "target")).FullName;
        var link = Path.Combine(root, "settings-link");

        try
        {
            try
            {
                Directory.CreateSymbolicLink(link, target);
            }
            catch (Exception exception)
                when (exception is IOException or UnauthorizedAccessException)
            {
                Skip.Test($"Symbolic links are unavailable: {exception.Message}");
            }

            var error = UserSettingsPathSecurity.ValidateExisting(
                new FileSystem(),
                link,
                directory: true
            );

            await Assert.That(error).Contains("cannot be a link or reparse point");
        }
        finally
        {
            if (Directory.Exists(link))
            {
                Directory.Delete(link);
            }

            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}
