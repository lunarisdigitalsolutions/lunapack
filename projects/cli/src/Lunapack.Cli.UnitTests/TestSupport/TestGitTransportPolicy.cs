using System.IO.Abstractions;
using Lunapack.Cli.Application;
using Lunapack.Cli.Application.Preferences;
using Lunapack.Cli.Project;
using Lunapack.Cli.Sources.Git;
using Lunapack.Cli.Trust;

namespace Lunapack.Cli.UnitTests.TestSupport;

internal static class TestGitTransportPolicy
{
    public static GitTransportPolicy Create(
        IFileSystem fileSystem,
        UserSettingsStore? userSettingsStore = null,
        CliConsole? console = null
    )
    {
        var effectiveUserSettingsStore =
            userSettingsStore
            ?? new UserSettingsStore(
                fileSystem,
                fileSystem.Path.Combine(fileSystem.Path.GetTempPath(), "lunapack-tests-profile")
            );
        return new GitTransportPolicy(
            new CliPreferenceService(
                fileSystem,
                new ProjectStateStore(fileSystem),
                effectiveUserSettingsStore
            ),
            console ?? TestConsole.Create()
        );
    }
}
