using SpectreTestConsole = Spectre.Console.Testing.TestConsole;

namespace Lunapack.Cli.UnitTests;

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
    public async Task Format_WhenLifecycleScriptsAreDeclared_DisplaysDescriptionsAndLiteralArgv()
    {
        var manifest = new PackManifest
        {
            Id = "example",
            Version = "1.0.0",
            Scripts = new PackManifest.PackScripts
            {
                PreInstall = new PackManifest.LifecycleScript
                {
                    File = "scripts/setup.ps1",
                    Runner = "pwsh",
                    Arguments = ["two words"],
                    Description = "Configure tooling",
                },
                PostUpdate = new PackManifest.LifecycleScript { Command = "dotnet" },
            },
        };
        var console = new SpectreTestConsole();

        foreach (var renderable in PackManifestInspectionFormatter.Format(manifest))
        {
            console.Write(renderable);
        }

        await Assert.That(console.Output).Contains("Configure tooling");
        await Assert.That(console.Output).Contains("pwsh scripts/setup.ps1 \"two words\"");
        await Assert.That(console.Output).Contains("dotnet");
        await Assert.That(console.Output).Contains("postInstall");
    }
}
