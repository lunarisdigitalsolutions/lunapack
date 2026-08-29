using System.Text.Json;
using Lunapack.Cli.Application.Serialization;
using Lunapack.Cli.Project;

namespace Lunapack.Cli.UnitTests.Application.Serialization;

public sealed class ProjectConfigurationSourceJsonConverterTests
{
    [Test]
    public async Task Serialize_WhenSourceIsLocal_WritesLocalContract()
    {
        ProjectConfiguration.Source source = new ProjectConfiguration.LocalSource
        {
            Name = "local",
            Path = "packs/catalog",
        };

        using var document = JsonDocument.Parse(
            JsonSerializer.Serialize(source, LunapackJsonSerializerOptions.Default)
        );
        var root = document.RootElement;

        await Assert.That(root.GetProperty("type").GetString()).IsEqualTo("local");
        await Assert.That(root.GetProperty("path").GetString()).IsEqualTo("packs/catalog");
        await Assert.That(root.EnumerateObject().Count()).IsEqualTo(2);
    }

    [Test]
    public async Task Serialize_WhenSourceIsGit_WritesConfiguredOptionalProperties()
    {
        ProjectConfiguration.Source source = new ProjectConfiguration.GitSource
        {
            Name = "engineering",
            Url = "https://example.test/engineering/packs.git",
            Ref = "refs/heads/main",
            Path = "packs",
            TimeoutSeconds = 30,
        };

        using var document = JsonDocument.Parse(
            JsonSerializer.Serialize(source, LunapackJsonSerializerOptions.Default)
        );
        var root = document.RootElement;

        await Assert.That(root.GetProperty("type").GetString()).IsEqualTo("git");
        await Assert
            .That(root.GetProperty("url").GetString())
            .IsEqualTo("https://example.test/engineering/packs.git");
        await Assert.That(root.GetProperty("ref").GetString()).IsEqualTo("refs/heads/main");
        await Assert.That(root.GetProperty("path").GetString()).IsEqualTo("packs");
        await Assert.That(root.GetProperty("timeoutSeconds").GetInt32()).IsEqualTo(30);
    }

    [Test]
    public async Task Serialize_WhenGitOptionalsAreAbsent_OmitsOptionalProperties()
    {
        ProjectConfiguration.Source source = new ProjectConfiguration.GitSource
        {
            Name = "engineering",
            Url = "https://example.test/engineering/packs.git",
        };

        using var document = JsonDocument.Parse(
            JsonSerializer.Serialize(source, LunapackJsonSerializerOptions.Default)
        );
        var root = document.RootElement;

        await Assert.That(root.TryGetProperty("ref", out _)).IsFalse();
        await Assert.That(root.TryGetProperty("path", out _)).IsFalse();
        await Assert.That(root.TryGetProperty("timeoutSeconds", out _)).IsFalse();
    }
}
