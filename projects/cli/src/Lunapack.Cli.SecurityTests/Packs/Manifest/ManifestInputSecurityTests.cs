using Lunapack.Cli.Packs.Manifest;
using Lunapack.Cli.Project;
using Lunapack.Cli.Sources;

namespace Lunapack.Cli.SecurityTests.Packs.Manifest;

public sealed class ManifestInputSecurityTests
{
    [Test]
    public async Task Validate_WhenManagedFileTargetBreaksProjectBoundary_ReturnsIssue()
    {
        var manifest = new PackManifest
        {
            Id = "malicious-input",
            Version = "1.0.0",
            ManagedFiles =
            [
                new PackManifest.PackManagedFile
                {
                    Path = "content.txt",
                    Target = "../outside.txt",
                },
            ],
        };

        var issues = ManifestModelValidator.Validate(manifest);

        await Assert.That(issues).Contains("Managed file target must stay inside the project.");
    }

    [Test]
    public async Task Validate_WhenConfigurationRemappingBreaksProjectBoundary_ReturnsIssue()
    {
        var configuration = new ProjectConfiguration
        {
            SchemaVersion = 1,
            Remap = new ProjectConfiguration.Remapping
            {
                Files = { ["README.md"] = "../outside.txt" },
            },
        };

        var issues = ManifestModelValidator.Validate(configuration);

        await Assert.That(issues).Contains("Managed file remappings must stay inside the project.");
    }

    [Test]
    public async Task Validate_WhenLockTargetBreaksProjectBoundary_ReturnsIssue()
    {
        var lockFile = new ProjectLockFile
        {
            SchemaVersion = 1,
            Packs =
            [
                new ProjectLockFile.ResolvedPack
                {
                    Id = "malicious-input",
                    Version = "1.0.0",
                    PackPath = "packs/malicious-input/1.0.0",
                    SourceName = "local",
                    SourcePath = "packs",
                    SourceIdentity = ConfiguredSourceIdentity.CreateLocal("packs"),
                    ManagedFiles =
                    [
                        new ProjectLockFile.ManagedFile
                        {
                            DeclaredTargetPath = "managed.txt",
                            TargetPath = "../outside.txt",
                            Sha256 = new string('0', 64),
                        },
                    ],
                },
            ],
        };

        var issues = ManifestModelValidator.Validate(lockFile);

        await Assert
            .That(issues)
            .Contains(
                "Resolved managed files must define safe declared and effective target paths and a SHA-256 hash."
            );
    }

    [Test]
    public async Task Validate_WhenProjectGitSourceEmbedsTokenOnlyUserInfo_ReturnsIssue()
    {
        var configuration = new ProjectConfiguration
        {
            SchemaVersion = 1,
            Sources =
            [
                new ProjectConfiguration.GitSource
                {
                    Name = "malicious",
                    Url = "https://placeholder-token@example.test/owner/packs.git",
                },
            ],
        };

        var issues = ManifestModelValidator.Validate(configuration);

        await Assert.That(issues).Contains("Git source 'malicious' URL is invalid.");
    }

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
