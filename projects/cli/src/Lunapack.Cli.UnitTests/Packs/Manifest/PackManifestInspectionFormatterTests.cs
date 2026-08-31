using Lunapack.Cli.Packs.Manifest;
using Lunapack.Cli.Project;
using SpectreTestConsole = Spectre.Console.Testing.TestConsole;

namespace Lunapack.Cli.UnitTests.Packs.Manifest;

public sealed class PackManifestInspectionFormatterTests
{
    [Test]
    public async Task Format_WhenTagsExceedPreviewLimit_DisplaysFirstFiveTags()
    {
        var manifest = new PackManifest
        {
            Id = "example",
            Version = "1.0.0",
            Tags = ["one", "two", "three", "four", "five", "six"],
        };
        var console = new SpectreTestConsole();

        foreach (var renderable in PackManifestInspectionFormatter.Format(manifest))
        {
            console.Write(renderable);
        }

        await Assert.That(console.Output).Contains("one, two, three, four, five");
        await Assert.That(console.Output).DoesNotContain("six");
    }

    [Test]
    public async Task Format_WhenGlobalFileRemappingMatches_DisplaysDeclaredAndEffectiveTarget()
    {
        var manifest = new PackManifest
        {
            Id = "example",
            Version = "1.0.0",
            ManagedFiles =
            [
                new PackManifest.PackManagedFile
                {
                    Source = "templates/template.md",
                    Target = "docs/adr/template.md",
                },
            ],
        };
        var remapping = new ProjectConfiguration.Remapping
        {
            Files = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["docs/adr/template.md"] = "docs/architecture/adr/_template.md",
            },
        };
        var console = new SpectreTestConsole();

        foreach (var renderable in PackManifestInspectionFormatter.Format(manifest, remapping))
        {
            console.Write(renderable);
        }

        await Assert
            .That(console.Output)
            .Contains("docs/adr/template.md -> docs/architecture/adr/_template.md");
        await Assert.That(console.Output).DoesNotContain("templates/template.md");
    }

    [Test]
    public async Task Format_WhenPackAndGlobalRemappingMatch_DisplaysPackEffectiveTarget()
    {
        var manifest = new PackManifest
        {
            Id = "example",
            Version = "1.0.0",
            ManagedFiles =
            [
                new PackManifest.PackManagedFile
                {
                    Source = "templates/template.md",
                    Target = "docs/adr/template.md",
                },
            ],
        };
        var packRemapping = new ProjectConfiguration.Remapping
        {
            Directories = { ["docs/adr"] = "docs/pack" },
        };
        var globalRemapping = new ProjectConfiguration.Remapping
        {
            Files = { ["docs/adr/template.md"] = "docs/global/template.md" },
        };
        var console = new SpectreTestConsole();

        foreach (
            var renderable in PackManifestInspectionFormatter.Format(
                manifest,
                packRemapping,
                globalRemapping
            )
        )
        {
            console.Write(renderable);
        }

        await Assert.That(console.Output).Contains("docs/adr/template.md -> docs/pack/template.md");
        await Assert.That(console.Output).DoesNotContain("docs/global/template.md");
    }

    [Test]
    public async Task Format_WhenReferencesContainSuppression_DisplaysDisabledHooksAndNone()
    {
        var manifest = new PackManifest
        {
            Id = "example",
            Version = "1.0.0",
            Packs =
            [
                new PackManifest.PackReference
                {
                    Id = "suppressed",
                    Version = "1.0.0",
                    DisabledHooks = ["preInstall", "postInstall"],
                },
                new PackManifest.PackReference { Id = "enabled", Version = "1.0.0" },
            ],
        };
        var console = new SpectreTestConsole();

        foreach (var renderable in PackManifestInspectionFormatter.Format(manifest))
        {
            console.Write(renderable);
        }

        await Assert.That(console.Output).Contains("preInstall, postInstall");
        await Assert.That(console.Output).Contains("none");
    }

    [Test]
    public async Task Format_WhenMixedLifecycleHooksAreDeclared_DisplaysOrderedTypedDetails()
    {
        var manifest = new PackManifest
        {
            Id = "example",
            Version = "1.0.0",
            Hooks = new PackManifest.PackHooks
            {
                PreInstall =
                [
                    new PackManifest.PackHook
                    {
                        Type = "instruction",
                        File = "instructions/setup.md",
                        Templating = true,
                    },
                    new PackManifest.PackHook
                    {
                        Type = "script",
                        File = "scripts/setup.ps1",
                        Runner = "pwsh",
                        Arguments = ["two words"],
                        Description = "Configure tooling",
                    },
                ],
                PostUpdate = [new PackManifest.PackHook { Type = "script", Command = "dotnet" }],
            },
        };
        var console = new SpectreTestConsole();
        console.Profile.Width = 500;

        foreach (var renderable in PackManifestInspectionFormatter.Format(manifest))
        {
            console.Write(renderable);
        }

        await Assert.That(console.Output).Contains("Configure tooling");
        await Assert.That(console.Output).Contains("pwsh scripts/setup.ps1 \"two words\"");
        await Assert.That(console.Output).Contains("instructions/setup.md; templating: enabled");
        await Assert.That(console.Output).Contains("dotnet");
        await Assert.That(console.Output).Contains("postInstall");
        await Assert
            .That(console.Output.IndexOf("instructions/setup.md", StringComparison.Ordinal))
            .IsLessThan(console.Output.IndexOf("pwsh scripts/setup.ps1", StringComparison.Ordinal));
    }

    [Test]
    public async Task Format_WhenNoLifecycleHooksAreDeclared_ReportsExplicitEmptyState()
    {
        var console = new SpectreTestConsole();

        foreach (
            var renderable in PackManifestInspectionFormatter.Format(
                new PackManifest { Id = "example", Version = "1.0.0" }
            )
        )
        {
            console.Write(renderable);
        }

        await Assert.That(console.Output).Contains("No lifecycle hooks declared.");
    }
}
