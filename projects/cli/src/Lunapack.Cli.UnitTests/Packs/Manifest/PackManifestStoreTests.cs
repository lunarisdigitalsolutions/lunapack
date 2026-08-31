using Lunapack.Cli.Packs.Manifest;

namespace Lunapack.Cli.UnitTests.Packs.Manifest;

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
        File.WriteAllText(
            path,
            "id: example\nversion: 1.0.0\nauthor: Example Author\nlicense: MIT\n"
        );
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
    public async Task Load_WhenParameterPropertyIsDuplicated_ReturnsFailure()
    {
        using var workspace = new TestWorkspace();
        File.WriteAllText(
            Path.Combine(workspace.Path, PackManifestStore.FileName),
            "id: example\nversion: 1.0.0\nauthor: Example\nlicense: MIT\nparameters:\n  environment:\n    type: string\n    type: bool\n"
        );
        var store = new PackManifestStore(workspace.FileSystem);

        var result = await store.LoadAsync(workspace.Path);

        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That(result.Error).Contains("Duplicate pack parameter property 'type'");
    }

    [Test]
    public async Task Load_WhenCompositeBindingIsStringArray_PreservesOrderedValues()
    {
        using var workspace = new TestWorkspace();
        File.WriteAllText(
            Path.Combine(workspace.Path, PackManifestStore.FileName),
            "id: example\nversion: 1.0.0\nauthor: Example\nlicense: MIT\npacks:\n  - id: child\n    version: 1.0.0\n    parameters:\n      features: [docker, api]\n"
        );
        var store = new PackManifestStore(workspace.FileSystem);

        var result = await store.LoadAsync(workspace.Path);

        await Assert.That(result.IsSuccess).IsTrue().Because(result.Error ?? string.Empty);
        await Assert
            .That(result.RequireValue().Packs.Single().Parameters["features"])
            .IsEquivalentTo(new List<string> { "docker", "api" });
    }

    [Test]
    public async Task Load_WhenMixedHooksDeclared_PreservesOrderAndNormalizesFiles()
    {
        using var workspace = new TestWorkspace();
        File.WriteAllText(
            Path.Combine(workspace.Path, PackManifestStore.FileName),
            "id: example\nversion: 1.0.0\nauthor: Example Author\nlicense: MIT\nhooks:\n  preInstall:\n    - type: instruction\n      file: instructions\\setup.md\n    - type: script\n      command: dotnet\n      arguments:\n        - tool\n        - restore\n    - type: script\n      file: scripts\\setup.ps1\n      runner: pwsh\n"
        );
        var store = new PackManifestStore(workspace.FileSystem);

        var result = await store.LoadAsync(workspace.Path);

        await Assert.That(result.IsSuccess).IsTrue();
        var hooks = result.RequireValue().Hooks.RequireNotNull().PreInstall.RequireNotNull();
        await Assert
            .That(string.Join(",", hooks.Select(hook => hook.Type)))
            .IsEqualTo("instruction,script,script");
        await Assert.That(hooks[0].File).IsEqualTo("instructions/setup.md");
        await Assert.That(hooks[2].File).IsEqualTo("scripts/setup.ps1");
        await Assert.That(hooks[0].Templating ?? false).IsFalse();
    }

    [Test]
    public async Task Update_WhenHooksSerialized_WritesTypedItemsInOrder()
    {
        using var workspace = new TestWorkspace();
        var path = Path.Combine(workspace.Path, PackManifestStore.FileName);
        File.WriteAllText(
            path,
            "id: example\nversion: 1.0.0\nauthor: Example Author\nlicense: MIT\nmanagedFiles:\n- source: README.md\n  target: README.md\n"
        );
        var store = new PackManifestStore(workspace.FileSystem);

        var result = await store.UpdateAsync(
            workspace.Path,
            manifest =>
            {
                manifest.Hooks = new PackManifest.PackHooks
                {
                    PostInstall =
                    [
                        new PackManifest.PackHook
                        {
                            Type = "instruction",
                            File = "instructions/setup.md",
                        },
                        new PackManifest.PackHook { Type = "script", Command = "dotnet" },
                    ],
                };
                return null;
            }
        );
        var contents = File.ReadAllText(path);

        await Assert.That(result.IsSuccess).IsTrue();
        await Assert.That(contents).Contains("hooks:");
        await Assert.That(contents).Contains("type: instruction");
        await Assert.That(contents).Contains("type: script");
        await Assert
            .That(contents.IndexOf("type: instruction", StringComparison.Ordinal))
            .IsLessThan(contents.IndexOf("type: script", StringComparison.Ordinal));
        await Assert.That(contents).DoesNotContain("scripts:");
    }

    [Test]
    public async Task Load_WhenLegacyScriptsDeclared_IsRejected()
    {
        using var workspace = new TestWorkspace();
        File.WriteAllText(
            Path.Combine(workspace.Path, PackManifestStore.FileName),
            "id: example\nversion: 1.0.0\nauthor: Example Author\nlicense: MIT\nscripts:\n  postInstall:\n    command: dotnet\n"
        );
        var store = new PackManifestStore(workspace.FileSystem);

        var result = await store.LoadAsync(workspace.Path);

        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That(result.Error).Contains("scripts");
    }

    [Test]
    public async Task Update_WhenMutationValid_PreservesUnrelatedModeledValues()
    {
        using var workspace = new TestWorkspace();
        var path = Path.Combine(workspace.Path, PackManifestStore.FileName);
        File.WriteAllText(
            path,
            "id: example\nname: Example\nversion: 1.0.0\nauthor: Example Author\nlicense: MIT\nparameters:\n  enabled:\n    type: bool\nmanagedFiles:\n- source: README.md\n  target: README.md\n"
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
        var loadedManifest = loaded.RequireValue();

        await Assert.That(result.IsSuccess).IsTrue();
        await Assert.That(loadedManifest.Name).IsEqualTo("Example");
        await Assert.That(loadedManifest.Parameters).ContainsKey("enabled");
        await Assert.That(loadedManifest.ManagedFiles.Single().Target).IsEqualTo("README.md");
    }
}
