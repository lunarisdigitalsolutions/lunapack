using System.IO.Abstractions;
using System.Security.Cryptography;
using Lunapack.Cli.Catalog;
using Lunapack.Cli.Packs.Lifecycle;
using Lunapack.Cli.Packs.Manifest;
using Lunapack.Cli.Sources;

namespace Lunapack.Cli.UnitTests.Packs.Lifecycle;

public sealed class PackedHookFileTests
{
    [Test]
    public async Task Resolve_WhenFileIsWithinSnapshot_BindsCanonicalPathAndDigest()
    {
        using var workspace = new TestWorkspace();
        var pack = CreatePack(workspace.Path);
        var hookFile = Path.Combine(pack.PackDirectory, "scripts", "setup.ps1");
        Directory.CreateDirectory(Path.GetDirectoryName(hookFile).RequireNotNull());
        File.WriteAllText(hookFile, "Write-Output setup");

        var result = PackedHookFile.Resolve(new FileSystem(), pack, "scripts/setup.ps1");

        await Assert.That(result.IsSuccess).IsTrue();
        await Assert.That(result.RequireValue().CanonicalPath).IsEqualTo(hookFile);
        await Assert
            .That(result.RequireValue().Sha256)
            .IsEqualTo(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(hookFile))));
    }

    [Test]
    public async Task Resolve_WhenFileEscapesSnapshot_ReturnsFailure()
    {
        using var workspace = new TestWorkspace();
        var pack = CreatePack(workspace.Path);

        var result = PackedHookFile.Resolve(new FileSystem(), pack, "../outside.ps1");

        await Assert.That(result.IsSuccess).IsFalse();
    }

    [Test]
    public async Task Verify_WhenPackedFileChanges_ReturnsFailure()
    {
        using var workspace = new TestWorkspace();
        var pack = CreatePack(workspace.Path);
        var hookFile = Path.Combine(pack.PackDirectory, "setup.ps1");
        File.WriteAllText(hookFile, "original");
        var packedFile = PackedHookFile.Resolve(new FileSystem(), pack, "setup.ps1").RequireValue();
        File.WriteAllText(hookFile, "changed");

        var result = packedFile.Verify(new FileSystem());

        await Assert.That(result.IsSuccess).IsFalse();
    }

    private static DiscoveredPack CreatePack(string root)
    {
        var snapshotRoot = Directory.CreateDirectory(Path.Combine(root, "snapshot")).FullName;
        var packDirectory = Directory
            .CreateDirectory(Path.Combine(snapshotRoot, "example"))
            .FullName;
        return new DiscoveredPack(
            snapshotRoot,
            packDirectory,
            new PackManifest { Id = "example", Version = "1.0.0" },
            "local",
            ConfiguredSourceIdentity.CreateLocal("source")
        );
    }
}
