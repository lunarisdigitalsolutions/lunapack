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
    public async Task Update_WhenAnotherWriterHoldsLock_PreservesOriginalBytes()
    {
        using var workspace = new TestWorkspace();
        var path = Path.Combine(workspace.Path, PackManifestStore.FileName);
        var lockPath = Path.Combine(workspace.Path, $".{PackManifestStore.FileName}.lock");
        const string original = "id: example\nversion: 1.0.0\n";
        File.WriteAllText(path, original);
        using var writeLock = File.Open(
            lockPath,
            FileMode.CreateNew,
            FileAccess.ReadWrite,
            FileShare.None
        );
        var store = new PackManifestStore(workspace.FileSystem);

        var result = await store.UpdateAsync(
            workspace.Path,
            manifest =>
            {
                manifest.Description = "competing write";
                return null;
            }
        );

        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That(File.ReadAllText(path)).IsEqualTo(original);
    }

    [Test]
    public async Task Update_WhenClosedLockFileExists_RecoversAndWritesManifest()
    {
        using var workspace = new TestWorkspace();
        var path = Path.Combine(workspace.Path, PackManifestStore.FileName);
        var lockPath = Path.Combine(workspace.Path, $".{PackManifestStore.FileName}.lock");
        File.WriteAllText(path, "id: example\nversion: 1.0.0\n");
        File.WriteAllText(lockPath, string.Empty);
        var store = new PackManifestStore(workspace.FileSystem);

        var result = await store.UpdateAsync(
            workspace.Path,
            manifest =>
            {
                manifest.Description = "Recovered";
                return null;
            }
        );

        await Assert.That(result.IsSuccess).IsTrue();
        await Assert.That(File.ReadAllText(path)).Contains("description: Recovered");
    }

    [Test]
    public async Task Load_WhenManifestInvalid_ReportsStableManifestLocations()
    {
        using var workspace = new TestWorkspace();
        var path = Path.Combine(workspace.Path, PackManifestStore.FileName);
        File.WriteAllText(
            path,
            "id: ''\nversion: invalid\nmanagedFiles:\n- source: ''\n  target: ''\n"
        );
        var store = new PackManifestStore(workspace.FileSystem);

        var result = await store.LoadAsync(workspace.Path);

        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That(result.Error).Contains("$.id:");
        await Assert.That(result.Error).Contains("$.version:");
        await Assert.That(result.Error).Contains("$.managedFiles:");
    }

    [Test]
    public async Task Load_WhenTagsAndEnumInvalid_ReportsCollectionLocations()
    {
        using var workspace = new TestWorkspace();
        var tags = string.Join('\n', Enumerable.Range(1, 16).Select(index => $"- tag-{index}"));
        File.WriteAllText(
            Path.Combine(workspace.Path, PackManifestStore.FileName),
            $"id: example\nversion: 1.0.0\ntags:\n{tags}\nparameters:\n  environment:\n    type: enum\n    values:\n    - dev\n    - dev\n"
        );
        var store = new PackManifestStore(workspace.FileSystem);

        var result = await store.LoadAsync(workspace.Path);

        await Assert.That(result.Error).Contains("$.tags:");
        await Assert.That(result.Error).Contains("$.parameters:");
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
