namespace Lunapack.Cli.UnitTests;

public sealed class PackManifestStoreTests
{
    [Test]
    public async Task Update_WhenCandidateInvalid_PreservesOriginalBytes()
    {
        using var workspace = new TestWorkspace();
        var path = Path.Combine(workspace.Path, PackManifestStore.FileName);
        const string original = "id: example\nversion: 1.0.0\n";
        File.WriteAllText(path, original);
        var store = new PackManifestStore(workspace.FileSystem);

        var result = await store.UpdateAsync(
            workspace.Path,
            manifest =>
            {
                manifest.Version = "invalid";
                return null;
            }
        );

        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That(File.ReadAllText(path)).IsEqualTo(original);
    }

    [Test]
    public async Task Update_WhenDestinationChanges_PreservesConcurrentContent()
    {
        using var workspace = new TestWorkspace();
        var path = Path.Combine(workspace.Path, PackManifestStore.FileName);
        const string original = "id: example\nversion: 1.0.0\n";
        const string concurrent = "id: concurrent\nversion: 2.0.0\n";
        File.WriteAllText(path, original);
        var store = new PackManifestStore(workspace.FileSystem);

        var result = await store.UpdateAsync(
            workspace.Path,
            manifest =>
            {
                manifest.Description = "stale write";
                File.WriteAllText(path, concurrent);
                return null;
            }
        );

        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That(File.ReadAllText(path)).IsEqualTo(concurrent);
    }

    [Test]
    public async Task Update_WhenMutationValid_PreservesUnrelatedModeledValues()
    {
        using var workspace = new TestWorkspace();
        var path = Path.Combine(workspace.Path, PackManifestStore.FileName);
        File.WriteAllText(
            path,
            "id: example\nname: Example\nversion: 1.0.0\nparameters:\n  enabled:\n    type: bool\nmanagedFiles:\n- source: README.md\n  target: README.md\n"
        );
        var store = new PackManifestStore(workspace.FileSystem);

        var result = await store.UpdateAsync(
            workspace.Path,
            manifest =>
            {
                manifest.Description = "Description";
                return null;
            }
        );
        var loaded = await store.LoadAsync(workspace.Path);

        await Assert.That(result.IsSuccess).IsTrue();
        await Assert.That(loaded.Value!.Name).IsEqualTo("Example");
        await Assert.That(loaded.Value.Parameters).ContainsKey("enabled");
        await Assert.That(loaded.Value.ManagedFiles.Single().Target).IsEqualTo("README.md");
    }
}
