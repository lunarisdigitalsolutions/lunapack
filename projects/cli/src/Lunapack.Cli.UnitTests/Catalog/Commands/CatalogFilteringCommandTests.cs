using SpectreTestConsole = Spectre.Console.Testing.TestConsole;

namespace Lunapack.Cli.UnitTests.Catalog.Commands;

public sealed class CatalogFilteringCommandTests
{
    [Test]
    [Arguments("discover")]
    [Arguments("search")]
    public async Task CatalogCommand_ByDefault_ShowsDirectInstallableRawAndCompositePacks(
        string command
    )
    {
        var ansiConsole = new SpectreTestConsole();
        using var workspace = new TestWorkspace(ansiConsole: ansiConsole);
        await ConfigureCatalogAsync(workspace);

        var exitCode = await workspace.Application.RunAsync(
            CreateArguments(command),
            workspace.Path
        );

        await Assert.That(exitCode).IsEqualTo(0);
        await Assert.That(ansiConsole.Output).Contains("raw-pack");
        await Assert.That(ansiConsole.Output).Contains("composite-pack");
        await Assert.That(ansiConsole.Output).DoesNotContain("hidden-pack");
        await Assert.That(ansiConsole.Output).DoesNotContain("hidden-composite");
    }

    [Test]
    [Arguments("discover")]
    [Arguments("search")]
    public async Task CatalogCommand_WhenOnlyCompositesSpecified_HidesRawAndDependencyOnlyPacks(
        string command
    )
    {
        var ansiConsole = new SpectreTestConsole();
        using var workspace = new TestWorkspace(ansiConsole: ansiConsole);
        await ConfigureCatalogAsync(workspace);

        var exitCode = await workspace.Application.RunAsync(
            CreateArguments(command, options: ["--only-composites", "--allow-draft"]),
            workspace.Path
        );

        await Assert.That(exitCode).IsEqualTo(0);
        await Assert.That(ansiConsole.Output).Contains("composite-pack");
        await Assert.That(ansiConsole.Output).DoesNotContain("raw-pack");
        await Assert.That(ansiConsole.Output).DoesNotContain("hidden-pack");
        await Assert.That(ansiConsole.Output).DoesNotContain("hidden-composite");
        await Assert.That(ansiConsole.Output).DoesNotContain("hidden-draft");
    }

    [Test]
    [Arguments("discover", "discover.only-composites")]
    [Arguments("search", "search.only-composites")]
    public async Task CatalogCommand_WhenOnlyCompositesPreferenceSet_ShowsDirectCompositesOnly(
        string command,
        string preferenceKey
    )
    {
        var ansiConsole = new SpectreTestConsole();
        using var workspace = new TestWorkspace(ansiConsole: ansiConsole);
        await ConfigureCatalogAsync(workspace);
        var setExitCode = await workspace.Application.RunAsync(
            ["config", "set", preferenceKey, "true"],
            workspace.Path
        );

        var exitCode = await workspace.Application.RunAsync(
            CreateArguments(command),
            workspace.Path
        );

        await Assert.That(setExitCode).IsEqualTo(0);
        await Assert.That(exitCode).IsEqualTo(0);
        await Assert.That(ansiConsole.Output).Contains("composite-pack");
        await Assert.That(ansiConsole.Output).DoesNotContain("raw-pack");
        await Assert.That(ansiConsole.Output).DoesNotContain("hidden-composite");
    }

    [Test]
    [Arguments("discover")]
    [Arguments("search")]
    public async Task CatalogCommand_WhenNewestReleaseIsDependencyOnly_UsesOlderEligibleRelease(
        string command
    )
    {
        var ansiConsole = new SpectreTestConsole();
        using var workspace = new TestWorkspace(ansiConsole: ansiConsole);
        CreatePack(workspace.Path, "release-one", CreateManifest("versioned-pack", "1.0.0"));
        CreatePack(
            workspace.Path,
            "release-two",
            CreateManifest("versioned-pack", "2.0.0", directInstallable: false)
        );
        await ConfigureWorkspaceAsync(workspace);

        var exitCode = await workspace.Application.RunAsync(
            CreateArguments(command, searchTerm: "versioned-pack"),
            workspace.Path
        );

        await Assert.That(exitCode).IsEqualTo(0);
        await Assert.That(ansiConsole.Output).Contains("1.0.0");
        await Assert.That(ansiConsole.Output).DoesNotContain("2.0.0");
    }

    [Test]
    public async Task InspectAndValidate_WhenPackIsDependencyOnly_StillShowAndValidateIt()
    {
        var ansiConsole = new SpectreTestConsole();
        using var workspace = new TestWorkspace(ansiConsole: ansiConsole);
        await ConfigureCatalogAsync(workspace);

        var inspectExitCode = await workspace.Application.RunAsync(
            ["inspect", "hidden-pack"],
            workspace.Path
        );
        var validateExitCode = await workspace.Application.RunAsync(
            ["validate", "hidden-pack"],
            workspace.Path
        );

        await Assert.That(inspectExitCode).IsEqualTo(0);
        await Assert.That(validateExitCode).IsEqualTo(0);
        await Assert.That(ansiConsole.Output).Contains("Direct installable");
        await Assert.That(ansiConsole.Output).Contains("no");
        await Assert.That(ansiConsole.Output).Contains("hidden-pack@1.0.0 is valid.");
    }

    [Test]
    public async Task Search_WhenOnlyCompositesPreferenceIsSet_PreservesMatchingLinkResults()
    {
        var ansiConsole = new SpectreTestConsole();
        using var workspace = new TestWorkspace(ansiConsole: ansiConsole);
        await ConfigureCatalogAsync(workspace);
        File.WriteAllText(Path.Combine(workspace.Path, "source", "guide.md"), "guide");
        var linkExitCode = await workspace.Application.RunAsync(
            [
                "links",
                "add",
                "guide-link",
                "--source",
                "local",
                "--include",
                "guide.md",
                "--target",
                "docs",
            ],
            workspace.Path
        );
        var preferenceExitCode = await workspace.Application.RunAsync(
            ["config", "set", "search.only-composites", "true"],
            workspace.Path
        );

        var searchExitCode = await workspace.Application.RunAsync(
            ["search", "guide"],
            workspace.Path
        );

        await Assert.That(linkExitCode).IsEqualTo(0);
        await Assert.That(preferenceExitCode).IsEqualTo(0);
        await Assert.That(searchExitCode).IsEqualTo(0);
        await Assert.That(ansiConsole.Output).Contains("guide-link");
        await Assert.That(ansiConsole.Output).Contains("0 matching packs and 1 matching links");
    }

    private static async Task ConfigureCatalogAsync(TestWorkspace workspace)
    {
        CreatePack(workspace.Path, "raw", CreateManifest("raw-pack", "1.0.0"));
        CreatePack(
            workspace.Path,
            "composite",
            CreateManifest("composite-pack", "1.0.0", dependencyId: "raw-pack")
        );
        CreatePack(
            workspace.Path,
            "hidden",
            CreateManifest("hidden-pack", "1.0.0", directInstallable: false)
        );
        CreatePack(
            workspace.Path,
            "hidden-composite",
            CreateManifest(
                "hidden-composite",
                "1.0.0",
                dependencyId: "hidden-pack",
                directInstallable: false
            )
        );
        CreatePack(
            workspace.Path,
            "hidden-draft",
            CreateManifest("hidden-draft", "1.0.0", directInstallable: false, draft: true)
        );
        await ConfigureWorkspaceAsync(workspace);
    }

    private static async Task ConfigureWorkspaceAsync(TestWorkspace workspace)
    {
        await workspace.Application.RunAsync(["init"], workspace.Path);
        await workspace.Application.RunAsync(
            ["sources", "add", "local", "local", "source"],
            workspace.Path
        );
    }

    private static string[] CreateArguments(
        string command,
        string? searchTerm = null,
        params string[] options
    )
    {
        var arguments = new List<string> { command };
        if (string.Equals(command, "search", StringComparison.Ordinal))
        {
            arguments.Add(searchTerm ?? "pack");
        }

        arguments.AddRange(options);
        return [.. arguments];
    }

    private static string CreateManifest(
        string id,
        string version,
        string? dependencyId = null,
        bool directInstallable = true,
        bool draft = false
    )
    {
        var directInstallableLine = directInstallable ? string.Empty : "directInstallable: false\n";
        var draftLine = draft ? "draft: true\n" : string.Empty;
        var packsBlock = dependencyId is null
            ? string.Empty
            : $"packs:\n  - id: {dependencyId}\n    version: 1.0.0\n";
        return $"id: {id}\nversion: {version}\nlicense: MIT\nauthor: Example Author\ndescription: {id}\n{directInstallableLine}{draftLine}{packsBlock}managedFiles:\n  - source: content.txt\n    target: {id}.txt\n";
    }

    private static void CreatePack(string projectDirectory, string folder, string manifest)
    {
        var packDirectory = Path.Combine(projectDirectory, "source", folder);
        Directory.CreateDirectory(packDirectory);
        File.WriteAllText(Path.Combine(packDirectory, "pack.yml"), manifest);
        File.WriteAllText(Path.Combine(packDirectory, "content.txt"), folder);
    }
}
