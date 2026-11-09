using Lunapack.Cli.Sources.Oci;

namespace Lunapack.Cli.UnitTests.Sources.Oci;

public sealed class NativeSourceCredentialStoreTests
{
    [Test]
    public async Task Scenario_SaveGetRemove_RoundTripsCredentialsAndClearsBuffers()
    {
        var backend = new MemoryCredentialBackend();
        var store = new NativeSourceCredentialStore(backend);
        var credential = new OciRegistryCredential("private-user", "secret-sentinel");

        var saved = store.Save("project/source/registry/path", credential);
        var read = store.Get("project/source/registry/path");
        var retrievedCredential = read.Value;
        var removed = store.Remove("project/source/registry/path");
        var afterRemoval = store.Get("project/source/registry/path").Value;

        await Assert.That(saved.IsSuccess).IsTrue();
        await Assert.That(saved.Value).IsTrue();
        await Assert.That(read.IsSuccess).IsTrue();
        await Assert.That(retrievedCredential).IsEqualTo(credential);
        await Assert.That(removed.IsSuccess).IsTrue();
        await Assert.That(removed.Value).IsTrue();
        await Assert.That(afterRemoval).IsNull();
        await Assert.That(backend.LastSavedPayload!.All(value => value == 0)).IsTrue();
        await Assert.That(backend.LastReturnedPayload!.All(value => value == 0)).IsTrue();
    }

    [Test]
    public async Task Scenario_DifferentSourceKeys_UseIsolatedHashedEntries()
    {
        var backend = new MemoryCredentialBackend();
        var store = new NativeSourceCredentialStore(backend);
        var firstCredential = new OciRegistryCredential("first-user", "first-secret");
        var secondCredential = new OciRegistryCredential("second-user", "second-secret");

        store.Save("project/source-a/registry/team-a", firstCredential);
        var firstIdentity = backend.LastIdentity;
        store.Save("project/source-b/registry/team-b", secondCredential);
        var secondIdentity = backend.LastIdentity;
        var retrievedFirst = store.Get("project/source-a/registry/team-a").Value;
        var retrievedSecond = store.Get("project/source-b/registry/team-b").Value;

        await Assert.That(firstIdentity).IsNotEqualTo(secondIdentity);
        await Assert.That(firstIdentity).DoesNotContain("source-a");
        await Assert.That(secondIdentity).DoesNotContain("source-b");
        await Assert.That(retrievedFirst).IsEqualTo(firstCredential);
        await Assert.That(retrievedSecond).IsEqualTo(secondCredential);
    }

    [Test]
    [Arguments(CredentialStoreOperation.Get)]
    [Arguments(CredentialStoreOperation.Save)]
    [Arguments(CredentialStoreOperation.Remove)]
    public async Task Scenario_NativeStoreFailure_ReturnsRedactedFailure(
        CredentialStoreOperation operation
    )
    {
        var backend = new MemoryCredentialBackend { FailOperation = operation };
        var store = new NativeSourceCredentialStore(backend);
        var error = operation switch
        {
            CredentialStoreOperation.Get => store.Get("source-key").Error,
            CredentialStoreOperation.Save => store
                .Save("source-key", new OciRegistryCredential("user-sentinel", "password-sentinel"))
                .Error,
            CredentialStoreOperation.Remove => store.Remove("source-key").Error,
            _ => throw new ArgumentOutOfRangeException(nameof(operation)),
        };

        await Assert.That(error).IsNotNull();
        await Assert.That(error).DoesNotContain("user-sentinel");
        await Assert.That(error).DoesNotContain("password-sentinel");
    }

    [Test]
    public async Task Scenario_InvalidStoredPayload_ReturnsFailureWithoutLeakingBytes()
    {
        var backend = new MemoryCredentialBackend();
        var store = new NativeSourceCredentialStore(backend);
        store.Get("source-key");
        backend.Set(backend.LastIdentity!, [1, 2, 3]);

        var result = store.Get("source-key");

        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That(result.Error).IsEqualTo("Stored registry credential is invalid.");
        await Assert.That(backend.LastReturnedPayload!.All(value => value == 0)).IsTrue();
    }

    [Test]
    public async Task Scenario_SecretPayload_EnforcesBoundedUtf8Serialization()
    {
        var credential = new OciRegistryCredential("user", new string('x', 64 * 1024 + 1));

        var result = new NativeSourceCredentialStore(new MemoryCredentialBackend()).Save(
            "source-key",
            credential
        );

        await Assert.That(result.IsSuccess).IsFalse();
        await Assert
            .That(result.Error)
            .IsEqualTo("Registry credential exceeds the supported storage limit.");
    }

    public enum CredentialStoreOperation
    {
        Get,
        Save,
        Remove,
    }

    private sealed class MemoryCredentialBackend : INativeCredentialBackend
    {
        private readonly Dictionary<string, byte[]> _values = new(StringComparer.Ordinal);

        public CredentialStoreOperation? FailOperation { get; init; }

        public string? LastIdentity { get; private set; }

        public byte[]? LastSavedPayload { get; private set; }

        public byte[]? LastReturnedPayload { get; private set; }

        public byte[]? Get(string identity)
        {
            FailIfRequested(CredentialStoreOperation.Get);
            LastIdentity = identity;
            if (!_values.TryGetValue(identity, out var value))
            {
                return null;
            }

            LastReturnedPayload = value.ToArray();
            return LastReturnedPayload;
        }

        public void Remove(string identity)
        {
            FailIfRequested(CredentialStoreOperation.Remove);
            LastIdentity = identity;
            _values.Remove(identity);
        }

        public void Save(string identity, byte[] payload)
        {
            FailIfRequested(CredentialStoreOperation.Save);
            LastIdentity = identity;
            LastSavedPayload = payload;
            _values[identity] = payload.ToArray();
        }

        public void Set(string identity, byte[] payload) => _values[identity] = payload.ToArray();

        private void FailIfRequested(CredentialStoreOperation operation)
        {
            if (FailOperation == operation)
            {
                throw new InvalidOperationException(
                    "Native credential store failed for password-sentinel."
                );
            }
        }
    }
}
