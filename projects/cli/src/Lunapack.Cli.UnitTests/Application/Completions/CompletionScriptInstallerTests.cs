using System.IO.Abstractions.TestingHelpers;
using Lunapack.Cli.Application.Completions;

namespace Lunapack.Cli.UnitTests.Application.Completions;

public sealed class CompletionScriptInstallerTests
{
    [Test]
    [Arguments("bash", true, "profile", ".bashrc")]
    [Arguments("fish", true, "profile", ".config/fish/conf.d/luna-completions.fish")]
    [Arguments("nushell", true, "appdata", "nushell/vendor/autoload/luna-completions.nu")]
    [Arguments(
        "nushell",
        false,
        "profile",
        ".local/share/nushell/vendor/autoload/luna-completions.nu"
    )]
    [Arguments("pwsh", true, "documents", "PowerShell/Microsoft.PowerShell_profile.ps1")]
    [Arguments("pwsh", false, "profile", ".config/powershell/Microsoft.PowerShell_profile.ps1")]
    [Arguments("zsh", true, "profile", ".zshrc")]
    public async Task CreatePlan_WhenShellIsSupported_UsesShellConfigurationPath(
        string shell,
        bool isWindows,
        string rootName,
        string relativePath
    )
    {
        var fileSystem = new MockFileSystem();
        var roots = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["profile"] = fileSystem.Path.Combine("C:", "profile"),
            ["documents"] = fileSystem.Path.Combine("C:", "documents"),
            ["appdata"] = fileSystem.Path.Combine("C:", "appdata"),
        };
        var resolver = CreateResolver(fileSystem, roots, isWindows);

        var plan = resolver.Resolve(shell).CreatePlan("script");

        var expectedPath = fileSystem.Path.Combine(
            roots[rootName],
            relativePath.Replace('/', fileSystem.Path.DirectorySeparatorChar)
        );
        await Assert.That(plan.DestinationPath).IsEqualTo(expectedPath);
    }

    [Test]
    public async Task Install_WhenScriptIsAlreadyPresent_DoesNotAppendDuplicate()
    {
        var fileSystem = new MockFileSystem();
        var profileDirectory = fileSystem.Path.Combine("C:", "profile");
        var installer = new BashCompletionScriptInstaller(fileSystem, profileDirectory);
        var plan = installer.CreatePlan("completion script\n");
        fileSystem.AddFile(plan.DestinationPath, new MockFileData("# existing"));

        installer.Install(plan);
        installer.Install(plan);

        await Assert
            .That(fileSystem.File.ReadAllText(plan.DestinationPath))
            .IsEqualTo($"# existing{Environment.NewLine}completion script\n");
    }

    [Test]
    public async Task CreatePlan_WhenNushellRunsOnMacOS_UsesApplicationSupportDataDirectory()
    {
        var fileSystem = new MockFileSystem();
        var profileDirectory = fileSystem.Path.Combine("C:", "profile");
        var installer = new NushellCompletionScriptInstaller(
            fileSystem,
            profileDirectory,
            fileSystem.Path.Combine("C:", "appdata"),
            null,
            isWindows: false,
            isMacOS: true
        );

        var plan = installer.CreatePlan("script");

        var expectedPath = fileSystem.Path.Combine(
            profileDirectory,
            "Library",
            "Application Support",
            "nushell",
            "vendor",
            "autoload",
            "luna-completions.nu"
        );
        await Assert.That(plan.DestinationPath).IsEqualTo(expectedPath);
    }

    [Test]
    public async Task CreatePlan_WhenXdgDataHomeIsSet_UsesConfiguredDataDirectory()
    {
        var fileSystem = new MockFileSystem();
        var profileDirectory = fileSystem.Path.Combine("C:", "profile");
        var xdgDataHomeDirectory = fileSystem.Path.GetFullPath("xdg-data");
        var installer = new NushellCompletionScriptInstaller(
            fileSystem,
            profileDirectory,
            fileSystem.Path.Combine("C:", "appdata"),
            xdgDataHomeDirectory,
            isWindows: false,
            isMacOS: true
        );

        var plan = installer.CreatePlan("script");

        var expectedPath = fileSystem.Path.Combine(
            xdgDataHomeDirectory,
            "nushell",
            "vendor",
            "autoload",
            "luna-completions.nu"
        );
        await Assert.That(plan.DestinationPath).IsEqualTo(expectedPath);
    }

    [Test]
    public async Task CreatePlan_WhenXdgDataHomeIsRelative_UsesPlatformDefaultDataDirectory()
    {
        var fileSystem = new MockFileSystem();
        var profileDirectory = fileSystem.Path.Combine("C:", "profile");
        var installer = new NushellCompletionScriptInstaller(
            fileSystem,
            profileDirectory,
            fileSystem.Path.Combine("C:", "appdata"),
            "relative-data",
            isWindows: false,
            isMacOS: false
        );

        var plan = installer.CreatePlan("script");

        var expectedPath = fileSystem.Path.Combine(
            profileDirectory,
            ".local",
            "share",
            "nushell",
            "vendor",
            "autoload",
            "luna-completions.nu"
        );
        await Assert.That(plan.DestinationPath).IsEqualTo(expectedPath);
    }

    private static CompletionScriptInstallerResolver CreateResolver(
        MockFileSystem fileSystem,
        Dictionary<string, string> roots,
        bool isWindows
    ) =>
        new([
            new BashCompletionScriptInstaller(fileSystem, roots["profile"]),
            new FishCompletionScriptInstaller(fileSystem, roots["profile"]),
            new NushellCompletionScriptInstaller(
                fileSystem,
                roots["profile"],
                roots["appdata"],
                null,
                isWindows,
                isMacOS: false
            ),
            new PowerShellCompletionScriptInstaller(
                fileSystem,
                roots["profile"],
                roots["documents"],
                isWindows
            ),
            new ZshCompletionScriptInstaller(fileSystem, roots["profile"]),
        ]);
}
