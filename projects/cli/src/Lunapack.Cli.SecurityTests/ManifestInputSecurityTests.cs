using Lunapack.Cli.Packs.Manifest;

namespace Lunapack.Cli.SecurityTests;

public sealed class ManifestInputSecurityTests
{
    [Test]
    [Arguments("path", "../secret.txt")]
    [Arguments("path", "/etc/shadow")]
    [Arguments("path", "C:\\Windows\\system.ini")]
    [Arguments("directory", "packs/../../outside")]
    [Arguments("directory", "\\\\server\\share")]
    public async Task Validate_WhenManagedFileSelectorBreaksSourceBoundary_ReturnsIssue(
        string selectorType,
        string selectorValue
    )
    {
        var managedFile = new PackManifest.PackManagedFile { Target = "managed-output" };
        if (string.Equals(selectorType, "path", StringComparison.Ordinal))
        {
            managedFile.Path = selectorValue;
        }
        else
        {
            managedFile.Directory = selectorValue;
        }

        var manifest = new PackManifest
        {
            Id = "malicious-input",
            Version = "1.0.0",
            ManagedFiles = [managedFile],
        };

        var issues = ManifestModelValidator.Validate(manifest);

        await Assert
            .That(
                issues.Any(issue =>
                    issue.Contains("must stay inside its source", StringComparison.Ordinal)
                )
            )
            .IsTrue();
    }
}
