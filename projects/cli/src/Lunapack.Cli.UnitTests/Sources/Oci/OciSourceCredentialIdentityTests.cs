using System.IO.Abstractions.TestingHelpers;
using Lunapack.Cli.Project;
using Lunapack.Cli.Sources.Oci;

namespace Lunapack.Cli.UnitTests.Sources.Oci;

public sealed class OciSourceCredentialIdentityTests
{
    private static readonly string _projectDirectory = OperatingSystem.IsWindows()
        ? @"C:\project"
        : "/project";

    [Test]
    public async Task Scenario_Create_ScopesCredentialsToProjectSourceAndRepositoryPath()
    {
        var fileSystem = new MockFileSystem();
        var otherProjectDirectory = fileSystem.Path.Combine(_projectDirectory, "other");
        fileSystem.AddDirectory(_projectDirectory);
        fileSystem.AddDirectory(otherProjectDirectory);
        var source = CreateSource("private", "https://registry.example.test", "team/packs");

        var identity = OciSourceCredentialIdentity
            .Create(fileSystem, _projectDirectory, source)
            .RequireValue();
        var normalizedIdentity = OciSourceCredentialIdentity
            .Create(
                fileSystem,
                _projectDirectory,
                CreateSource("private", "https://REGISTRY.EXAMPLE.TEST/", "team/packs")
            )
            .RequireValue();
        var renamedIdentity = OciSourceCredentialIdentity
            .Create(
                fileSystem,
                _projectDirectory,
                CreateSource("renamed", "https://registry.example.test", "team/packs")
            )
            .RequireValue();
        var differentPathIdentity = OciSourceCredentialIdentity
            .Create(
                fileSystem,
                _projectDirectory,
                CreateSource("private", "https://registry.example.test", "other/packs")
            )
            .RequireValue();
        var differentProjectIdentity = OciSourceCredentialIdentity
            .Create(fileSystem, otherProjectDirectory, source)
            .RequireValue();

        await Assert.That(normalizedIdentity).IsEqualTo(identity);
        await Assert.That(renamedIdentity).IsNotEqualTo(identity);
        await Assert.That(differentPathIdentity).IsNotEqualTo(identity);
        await Assert.That(differentProjectIdentity).IsNotEqualTo(identity);
    }

    private static ProjectConfiguration.OciSource CreateSource(
        string name,
        string url,
        string path
    ) =>
        new()
        {
            Name = name,
            Url = url,
            Path = path,
        };
}
