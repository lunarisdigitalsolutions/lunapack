using System.IO.Abstractions;
using Lunapack.Cli.Application;
using Lunapack.Cli.Application.Completions;
using Lunapack.Cli.Packs;
using Lunapack.Cli.Project;
using Lunapack.Cli.Sources.Git;
using Lunapack.Cli.Trust;
using Spectre.Console;

namespace Lunapack.Cli.UnitTests;

internal sealed class TestWorkspace : IDisposable
{
    public TestWorkspace(
        IPackUpdatePrompter? packUpdatePrompter = null,
        IAnsiConsole? ansiConsole = null,
        IGitProcessRunner? gitProcessRunner = null,
        ITrustConfirmer? trustConfirmer = null
    )
    {
        Path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "lunapack-tests",
            Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(Path);
        FileSystem = new FileSystem();
        var profileDirectory = System.IO.Path.Combine(Path, "profile");
        Application = new CliApplication(
            FileSystem,
            ansiConsole ?? TestConsole.CreateAnsiConsole(),
            packUpdatePrompter,
            gitProcessRunner: gitProcessRunner ?? new StubGitProcessRunner(),
            trustConfirmer: trustConfirmer,
            userSettingsStore: new UserSettingsStore(FileSystem, profileDirectory),
            completionScriptInstallerResolver: new CompletionScriptInstallerResolver([
                new BashCompletionScriptInstaller(FileSystem, profileDirectory),
                new FishCompletionScriptInstaller(FileSystem, profileDirectory),
                new NushellCompletionScriptInstaller(
                    FileSystem,
                    profileDirectory,
                    System.IO.Path.Combine(profileDirectory, "AppData", "Roaming"),
                    null,
                    isWindows: true,
                    isMacOS: false
                ),
                new PowerShellCompletionScriptInstaller(
                    FileSystem,
                    profileDirectory,
                    System.IO.Path.Combine(profileDirectory, "Documents"),
                    isWindows: true
                ),
                new ZshCompletionScriptInstaller(FileSystem, profileDirectory),
            ])
        );
        ManifestStore = new ProjectManifestStore(FileSystem);
        StateStore = new ProjectStateStore(FileSystem);
    }

    public CliApplication Application { get; }

    public IFileSystem FileSystem { get; }

    public ProjectManifestStore ManifestStore { get; }

    public ProjectStateStore StateStore { get; }

    public string Path { get; }

    public void Dispose()
    {
        if (Directory.Exists(Path))
        {
            Directory.Delete(Path, recursive: true);
        }
    }
}
