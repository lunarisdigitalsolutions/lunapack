using SpectreTestConsole = Spectre.Console.Testing.TestConsole;

namespace Lunapack.Cli.UnitTests;

[NotInParallel]
public sealed class CliHelpDocumentationTests
{
    private static readonly HelpContract[] _commands =
    [
        new("init"),
        new("variables list"),
        new("variables set", "<name> <value>"),
        new("variables rm", "<name>"),
        new("remap list"),
        new("remap set", "<directory|file> <target> <new-target>"),
        new("remap rm", "<directory|file> <target>"),
        new("sources add local", "<name> <path>"),
        new("sources add git", "<name> <repository-url>", ["--ref", "-r", "--path", "-p"]),
        new(
            "sources add github",
            "<name> <organization/repository>",
            ["--ref", "-r", "--path", "-p"]
        ),
        new("sources list"),
        new("sources remove", "<name>"),
        new("trust source", "<name>...", ["--project", "--global"]),
        new("trust pack", "<id>... --source <name>", ["--source", "-s", "--project", "--global"]),
        new("trust list", null, ["--global"]),
        new("trust revoke source", "<name>...", ["--project", "--global"]),
        new(
            "trust revoke pack",
            "<id>... --source <name>",
            ["--source", "-s", "--project", "--global"]
        ),
        new("discover", null, ["--versions", "-v"]),
        new("search", "<query>", ["--versions", "-v"]),
        new("validate", "<pack-reference>"),
        new("inspect", "<pack-reference>"),
        new(
            "install",
            "<pack-reference> [<pack-reference>...]",
            [
                "--dry-run",
                "-D",
                "--destination",
                "-d",
                "--adopt-existing",
                "-a",
                "--parameter",
                "-p",
                "--no-variables",
                "-nv",
                "--skip-variable",
                "-sv",
                "--scripts",
            ]
        ),
        new("uninstall", "<pack-reference> [<pack-reference>...]"),
        new("outdated"),
        new("update", "[<pack-reference>...]", ["--dry-run", "-D", "--prompt", "-p", "--scripts"]),
        new("mv", "<source> <target>"),
        new("audit"),
    ];

    [Test]
    public async Task PublicCommands_WhenDocumented_MatchLiveHelp()
    {
        var documentation = await File.ReadAllTextAsync(
            Path.Combine(AppContext.BaseDirectory, "TestData", "commands.md")
        );

        foreach (var contract in _commands)
        {
            using var workspace = new TestWorkspace(ansiConsole: new SpectreTestConsole());
            var commandPath = contract.Command.Split(' ', StringSplitOptions.RemoveEmptyEntries);

            await using var output = new StringWriter();
            var exitCode = await workspace.Application.RunAsync(
                [.. commandPath, "--help"],
                workspace.Path,
                output
            );

            await Assert.That(exitCode).IsEqualTo(0).Because(contract.Command);
            await Assert
                .That(documentation)
                .Contains($"`luna {contract.Command}{FormatArguments(contract.Arguments)}");
            foreach (var option in contract.Options)
            {
                await Assert.That(output.ToString()).Contains(option).Because(contract.Command);
                await Assert.That(documentation).Contains(option).Because(contract.Command);
            }
        }
    }

    [Test]
    public async Task GlobalOptions_WhenDocumented_MatchLiveHelp()
    {
        var documentation = await File.ReadAllTextAsync(
            Path.Combine(AppContext.BaseDirectory, "TestData", "commands.md")
        );
        using var workspace = new TestWorkspace(ansiConsole: new SpectreTestConsole());

        await using var output = new StringWriter();
        var exitCode = await workspace.Application.RunAsync(["--help"], workspace.Path, output);

        await Assert.That(exitCode).IsEqualTo(0);
        foreach (var option in new[] { "--workspace", "-w", "--log-level", "-ll", "--help" })
        {
            await Assert.That(output.ToString()).Contains(option);
            await Assert.That(documentation).Contains(option);
        }
    }

    private static string FormatArguments(string? arguments) =>
        arguments is null ? string.Empty : $" {arguments}";

    private sealed record HelpContract(
        string Command,
        string? Arguments = null,
        IReadOnlyList<string>? DocumentedOptions = null
    )
    {
        public IReadOnlyList<string> Options { get; } = DocumentedOptions ?? [];
    }
}
