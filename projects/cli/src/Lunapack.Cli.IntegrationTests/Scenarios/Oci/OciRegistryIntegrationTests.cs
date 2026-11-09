using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Lunapack.Cli.IntegrationTests;

[NotInParallel]
public sealed class OciRegistryIntegrationTests
{
    private const string SourceName = "integration";
    private const string SourcePath = "lunapack-integration";
    private const string OciManifestMediaType = "application/vnd.oci.image.manifest.v1+json";
    private const string PackArtifactMediaType = "application/vnd.lunapack.pack.v1";
    private const string PackConfigMediaType = "application/vnd.lunapack.pack.config.v1+json";
    private const string PackContentMediaType = "application/vnd.lunapack.pack.content.v1+zip";

    [Test]
    public async Task Scenario_PrivatePackExample_ConfigurationAndManifestsValidate()
    {
        var sampleRoot = Path.Combine(GetRepositoryRoot(), "examples", "oci-private-packs");
        using var workspace = new TestWorkspace();
        var initialized = await CliProcess.InvokeAsync(workspace.Path, "init");
        await Assert.That(initialized.ExitCode).IsEqualTo(0).Because(Describe(initialized));
        File.Copy(
            Path.Combine(sampleRoot, "lunapack.yml"),
            Path.Combine(workspace.Path, "lunapack.yml"),
            overwrite: true
        );
        var sources = await CliProcess.InvokeAsync(workspace.Path, "sources", "list");

        await Assert.That(sources.ExitCode).IsEqualTo(0).Because(Describe(sources));
        await Assert.That(sources.StandardOutput).Contains("ghcr");
        await Assert.That(sources.StandardOutput).Contains("acr");
        await Assert.That(sources.StandardOutput).Contains("harbor");
        await Assert.That(sources.StandardOutput).Contains("generic");

        foreach (var packName in new[] { "backend", "security" })
        {
            var packDirectory = Path.Combine(sampleRoot, "packs", packName);
            var validation = await CliProcess.InvokeAsync(packDirectory, "pack", "validate");

            await Assert.That(validation.ExitCode).IsEqualTo(0).Because(Describe(validation));
        }
    }

    [Test]
    public async Task Scenario_OciDistribution_PublishesDiscoversInstallsUpdatesAndRejectsInvalidArtifacts()
    {
        var registryUrl = Environment.GetEnvironmentVariable("LUNAPACK_TEST_OCI_REGISTRY");
        if (string.IsNullOrWhiteSpace(registryUrl))
        {
            return;
        }

        var (username, password) = GetRegistryCredentials();
        var registry = new Uri(registryUrl.TrimEnd('/') + "/", UriKind.Absolute);
        var packId = $"oci-integration-{Guid.NewGuid():N}";
        var repository = $"{SourcePath}/{packId}";
        using var workspace = new TestWorkspace();
        var (packDirectory, temporaryDirectory, environment) = CreateTestWorkspace(
            workspace,
            packId,
            username,
            password
        );

        await InitializeProjectAsync(workspace.Path, environment, registryUrl);
        var firstDigest = await PublishPackVersionAsync(
            workspace.Path,
            environment,
            packDirectory,
            packId,
            "1.0.0",
            "first release",
            registry,
            repository,
            username,
            password
        );
        var installedFile = await VerifyDiscoveryAndInstallAsync(
            workspace.Path,
            environment,
            packId,
            firstDigest
        );
        var (secondDigest, updatedLock) = await PublishAndApplyUpdateAsync(
            workspace.Path,
            environment,
            packDirectory,
            packId,
            installedFile,
            firstDigest,
            registry,
            repository,
            username,
            password
        );
        await VerifyDuplicatePublicationAsync(
            workspace.Path,
            environment,
            packId,
            secondDigest,
            registry,
            repository,
            username,
            password
        );
        await VerifyInvalidArtifactsAsync(
            workspace.Path,
            environment,
            packDirectory,
            packId,
            installedFile,
            updatedLock,
            registry,
            repository,
            username,
            password
        );

        await AssertTemporaryWorkspacesAreCleanAsync(temporaryDirectory);
    }

    private static (string Username, string Password) GetRegistryCredentials()
    {
        var username = Environment.GetEnvironmentVariable("LUNAPACK_TEST_OCI_USERNAME");
        var password = Environment.GetEnvironmentVariable("LUNAPACK_TEST_OCI_PASSWORD");
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrEmpty(password))
        {
            throw new InvalidOperationException(
                "The OCI integration registry requires test username and password values."
            );
        }

        return (username, password);
    }

    private static (
        string PackDirectory,
        string TemporaryDirectory,
        Dictionary<string, string?> Environment
    ) CreateTestWorkspace(TestWorkspace workspace, string packId, string username, string password)
    {
        var temporaryDirectory = Path.Combine(workspace.Path, "temporary");
        var profileDirectory = Path.Combine(workspace.Path, "profile");
        var packDirectory = Path.Combine(workspace.Path, "packs", packId);
        Directory.CreateDirectory(temporaryDirectory);
        Directory.CreateDirectory(profileDirectory);
        var environment = CreateCliEnvironment(
            profileDirectory,
            temporaryDirectory,
            username,
            password
        );
        return (packDirectory, temporaryDirectory, environment);
    }

    private static async Task AssertTemporaryWorkspacesAreCleanAsync(string temporaryDirectory)
    {
        var workspaceRoot = Path.Combine(temporaryDirectory, "lunapack");
        var unexpectedRootEntries = Directory
            .EnumerateFileSystemEntries(temporaryDirectory)
            .Where(entry =>
                !string.Equals(
                    Path.GetFileName(entry),
                    Path.GetFileName(workspaceRoot),
                    StringComparison.OrdinalIgnoreCase
                )
            )
            .ToArray();
        await Assert.That(unexpectedRootEntries).IsEmpty();

        var remainingWorkspaces = Directory.Exists(workspaceRoot)
            ? Directory
                .EnumerateDirectories(workspaceRoot)
                .SelectMany(directory =>
                    Directory.EnumerateFileSystemEntries(
                        directory,
                        "*",
                        SearchOption.AllDirectories
                    )
                )
                .Concat(Directory.EnumerateFiles(workspaceRoot))
                .ToArray()
            : [];
        await Assert.That(remainingWorkspaces).IsEmpty();
    }

    private static async Task InitializeProjectAsync(
        string projectDirectory,
        IReadOnlyDictionary<string, string?> environment,
        string registryUrl
    )
    {
        var initialized = await RunCliAsync(projectDirectory, environment, "init");
        await AssertSuccessfulCliAsync(initialized);
        var source = await RunCliAsync(
            projectDirectory,
            environment,
            "sources",
            "add",
            "oci",
            SourceName,
            registryUrl,
            "--path",
            SourcePath
        );
        await AssertSuccessfulCliAsync(source);
    }

    private static async Task<string> PublishPackVersionAsync(
        string projectDirectory,
        IReadOnlyDictionary<string, string?> environment,
        string packDirectory,
        string packId,
        string version,
        string content,
        Uri registry,
        string repository,
        string username,
        string password
    )
    {
        WritePack(packDirectory, packId, version, content);
        var published = await RunCliAsync(
            projectDirectory,
            environment,
            "publish",
            $"packs/{packId}",
            "--target",
            SourceName
        );
        await AssertSuccessfulCliAsync(published);
        var digest = ReadManifestDigest(published.StandardOutput);
        await Assert
            .That(
                await ReadRegistryManifestDigestAsync(
                    registry,
                    repository,
                    version,
                    username,
                    password
                )
            )
            .IsEqualTo(digest);
        return digest;
    }

    private static async Task<string> VerifyDiscoveryAndInstallAsync(
        string projectDirectory,
        IReadOnlyDictionary<string, string?> environment,
        string packId,
        string manifestDigest
    )
    {
        var discovery = await RunCliAsync(projectDirectory, environment, "discover");
        await AssertSuccessfulCliAsync(discovery);
        await Assert
            .That(NormalizePackColumnOutput(discovery.StandardOutput))
            .Contains(NormalizePackId(packId));
        await Assert.That(discovery.StandardOutput).Contains("1.0.0");

        var search = await RunCliAsync(
            projectDirectory,
            environment,
            "search",
            packId,
            "--versions",
            "10"
        );
        await AssertSuccessfulCliAsync(search);
        await Assert.That(search.StandardOutput).Contains("1.0.0");

        var inspection = await RunCliAsync(
            projectDirectory,
            environment,
            "inspect",
            $"{packId}@1.0.0"
        );
        await AssertSuccessfulCliAsync(inspection);
        await Assert.That(inspection.StandardOutput).Contains("Integration pack");

        var installed = await RunCliAsync(
            projectDirectory,
            environment,
            "install",
            $"{packId}@1.0.0",
            "--scripts",
            "skip"
        );
        await AssertSuccessfulCliAsync(installed);
        var installedFile = Path.Combine(projectDirectory, "installed", "message.txt");
        await Assert.That(File.ReadAllText(installedFile)).IsEqualTo("first release");
        var lockFile = File.ReadAllText(Path.Combine(projectDirectory, "lunapack-lock.yml"));
        await Assert.That(lockFile).Contains(manifestDigest);
        return installedFile;
    }

    private static async Task<(string ManifestDigest, string LockFile)> PublishAndApplyUpdateAsync(
        string projectDirectory,
        IReadOnlyDictionary<string, string?> environment,
        string packDirectory,
        string packId,
        string installedFile,
        string previousDigest,
        Uri registry,
        string repository,
        string username,
        string password
    )
    {
        var manifestDigest = await PublishPackVersionAsync(
            projectDirectory,
            environment,
            packDirectory,
            packId,
            "1.1.0",
            "second release",
            registry,
            repository,
            username,
            password
        );
        await Assert.That(manifestDigest).IsNotEqualTo(previousDigest);

        var versions = await RunCliAsync(
            projectDirectory,
            environment,
            "search",
            packId,
            "--versions",
            "10"
        );
        await AssertSuccessfulCliAsync(versions);
        await Assert.That(versions.StandardOutput).Contains("1.0.0");
        await Assert.That(versions.StandardOutput).Contains("1.1.0");

        var outdated = await RunCliAsync(projectDirectory, environment, "outdated");
        await AssertSuccessfulCliAsync(outdated);
        await Assert.That(outdated.StandardOutput).Contains("1.1.0");

        var updated = await RunCliAsync(projectDirectory, environment, "update", packId);
        await AssertSuccessfulCliAsync(updated);
        await Assert.That(File.ReadAllText(installedFile)).IsEqualTo("second release");
        var lockFile = File.ReadAllText(Path.Combine(projectDirectory, "lunapack-lock.yml"));
        await Assert.That(lockFile).Contains(manifestDigest);
        await Assert.That(lockFile).DoesNotContain(previousDigest);
        return (manifestDigest, lockFile);
    }

    private static async Task VerifyDuplicatePublicationAsync(
        string projectDirectory,
        IReadOnlyDictionary<string, string?> environment,
        string packId,
        string manifestDigest,
        Uri registry,
        string repository,
        string username,
        string password
    )
    {
        var duplicate = await RunCliAsync(
            projectDirectory,
            environment,
            "publish",
            $"packs/{packId}",
            "--target",
            SourceName
        );
        await Assert.That(duplicate.ExitCode == 0).IsFalse().Because(Describe(duplicate));
        await Assert
            .That(
                await ReadRegistryManifestDigestAsync(
                    registry,
                    repository,
                    "1.1.0",
                    username,
                    password
                )
            )
            .IsEqualTo(manifestDigest);
    }

    private static async Task VerifyInvalidArtifactsAsync(
        string projectDirectory,
        IReadOnlyDictionary<string, string?> environment,
        string packDirectory,
        string packId,
        string installedFile,
        string lockFile,
        Uri registry,
        string repository,
        string username,
        string password
    )
    {
        await PushRegistryArtifactAsync(
            registry,
            repository,
            "1.2.0",
            packId,
            "1.2.0",
            artifactType: PackArtifactMediaType,
            configMediaType: PackConfigMediaType,
            config: CreatePackConfig(packId, "1.2.0"),
            content: Encoding.UTF8.GetBytes("not a ZIP archive"),
            username,
            password
        );
        await PushRegistryArtifactAsync(
            registry,
            repository,
            "1.3.0",
            packId,
            "1.3.0",
            artifactType: "application/vnd.example.foreign.v1",
            configMediaType: "application/vnd.oci.empty.v1+json",
            config: "{}"u8.ToArray(),
            content: null,
            username,
            password
        );
        var wrongTypeDigest = await ReadRegistryManifestDigestAsync(
            registry,
            repository,
            "1.3.0",
            username,
            password
        );

        var corruptValidation = await RunCliAsync(
            projectDirectory,
            environment,
            "validate",
            $"{packId}@1.2.0"
        );
        await Assert.That(corruptValidation.ExitCode == 0).IsFalse();
        var wrongTypeInstall = await RunCliAsync(
            projectDirectory,
            environment,
            "install",
            $"{packId}@1.3.0",
            "--scripts",
            "skip"
        );
        await Assert.That(wrongTypeInstall.ExitCode == 0).IsFalse();
        await Assert.That(File.ReadAllText(installedFile)).IsEqualTo("second release");
        await Assert
            .That(File.ReadAllText(Path.Combine(projectDirectory, "lunapack-lock.yml")))
            .IsEqualTo(lockFile);

        WritePack(packDirectory, packId, "1.3.0", "replacement");
        var wrongTypePublish = await RunCliAsync(
            projectDirectory,
            environment,
            "publish",
            $"packs/{packId}",
            "--target",
            SourceName
        );
        await Assert.That(wrongTypePublish.ExitCode == 0).IsFalse();
        await Assert
            .That(
                await ReadRegistryManifestDigestAsync(
                    registry,
                    repository,
                    "1.3.0",
                    username,
                    password
                )
            )
            .IsEqualTo(wrongTypeDigest);
    }

    private static Dictionary<string, string?> CreateCliEnvironment(
        string profileDirectory,
        string temporaryDirectory,
        string username,
        string password
    ) =>
        new(StringComparer.Ordinal)
        {
            ["LUNAPACK_USER_PROFILE"] = profileDirectory,
            ["LUNAPACK_OCI_SOURCE"] = SourceName,
            ["LUNAPACK_OCI_USERNAME"] = username,
            ["LUNAPACK_OCI_PASSWORD"] = password,
            ["TMPDIR"] = temporaryDirectory,
            ["TEMP"] = temporaryDirectory,
            ["TMP"] = temporaryDirectory,
        };

    private static Task<CliResult> RunCliAsync(
        string projectDirectory,
        IReadOnlyDictionary<string, string?> environment,
        params string[] arguments
    ) => CliProcess.InvokeAsync(projectDirectory, environment, arguments);

    private static async Task AssertSuccessfulCliAsync(CliResult result) =>
        await Assert.That(result.ExitCode).IsEqualTo(0).Because(Describe(result));

    private static async Task PushRegistryArtifactAsync(
        Uri registry,
        string repository,
        string tag,
        string packId,
        string version,
        string artifactType,
        string configMediaType,
        byte[] config,
        byte[]? content,
        string username,
        string password
    )
    {
        using var client = CreateRegistryClient(username, password);
        await UploadBlobAsync(client, registry, repository, config);
        var layers = new List<Dictionary<string, object>>();
        if (content is not null)
        {
            await UploadBlobAsync(client, registry, repository, content);
            layers.Add(
                new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    ["mediaType"] = PackContentMediaType,
                    ["digest"] = ComputeDigest(content),
                    ["size"] = content.LongLength,
                }
            );
        }

        var manifest = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["schemaVersion"] = 2,
            ["mediaType"] = OciManifestMediaType,
            ["artifactType"] = artifactType,
            ["config"] = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["mediaType"] = configMediaType,
                ["digest"] = ComputeDigest(config),
                ["size"] = config.LongLength,
            },
            ["layers"] = layers,
            ["annotations"] = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["io.lunapack.pack.id"] = packId,
                ["io.lunapack.pack.format-version"] = "1",
                ["org.opencontainers.image.version"] = version,
            },
        };
        var manifestContent = JsonSerializer.SerializeToUtf8Bytes(manifest);
        using var manifestRequest = new HttpRequestMessage(
            HttpMethod.Put,
            GetManifestUri(registry, repository, tag)
        )
        {
            Content = new ByteArrayContent(manifestContent),
        };
        manifestRequest.Content.Headers.ContentType = new MediaTypeHeaderValue(
            OciManifestMediaType
        );
        using var manifestResponse = await client.SendAsync(manifestRequest);
        manifestResponse.EnsureSuccessStatusCode();
    }

    private static async Task UploadBlobAsync(
        HttpClient client,
        Uri registry,
        string repository,
        byte[] content
    )
    {
        var uploadUri = new Uri(registry, $"v2/{repository}/blobs/uploads/");
        using var start = await client.PostAsync(uploadUri, content: null);
        start.EnsureSuccessStatusCode();
        var location =
            start.Headers.Location
            ?? throw new InvalidOperationException(
                "The registry omitted the blob upload location."
            );
        var target = location.IsAbsoluteUri ? location : new Uri(registry, location);
        var query = target.Query.TrimStart('?');
        var builder = new UriBuilder(target)
        {
            Query = string.IsNullOrEmpty(query)
                ? $"digest={Uri.EscapeDataString(ComputeDigest(content))}"
                : $"{query}&digest={Uri.EscapeDataString(ComputeDigest(content))}",
        };
        using var body = new ByteArrayContent(content);
        using var upload = await client.PutAsync(builder.Uri, body);
        upload.EnsureSuccessStatusCode();
    }

    private static async Task<string> ReadRegistryManifestDigestAsync(
        Uri registry,
        string repository,
        string tag,
        string username,
        string password
    )
    {
        using var client = CreateRegistryClient(username, password);
        using var request = new HttpRequestMessage(
            HttpMethod.Head,
            GetManifestUri(registry, repository, tag)
        );
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(OciManifestMediaType));
        using var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        if (response.Headers.TryGetValues("Docker-Content-Digest", out var digests))
        {
            return digests.Single();
        }

        if (response.Content.Headers.TryGetValues("Docker-Content-Digest", out digests))
        {
            return digests.Single();
        }

        throw new InvalidOperationException("The registry omitted Docker-Content-Digest.");
    }

    private static HttpClient CreateRegistryClient(string username, string password)
    {
        var client = new HttpClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Basic",
            Convert.ToBase64String(Encoding.UTF8.GetBytes($"{username}:{password}"))
        );
        return client;
    }

    private static Uri GetManifestUri(Uri registry, string repository, string reference) =>
        new(registry, $"v2/{repository}/manifests/{reference}");

    private static byte[] CreatePackConfig(string packId, string version) =>
        JsonSerializer.SerializeToUtf8Bytes(
            new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["id"] = packId,
                ["version"] = version,
                ["formatVersion"] = 1,
            }
        );

    private static string ComputeDigest(byte[] content) =>
        $"sha256:{Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant()}";

    private static void WritePack(
        string packDirectory,
        string packId,
        string version,
        string content
    )
    {
        var filesDirectory = Path.Combine(packDirectory, "files");
        Directory.CreateDirectory(filesDirectory);
        File.WriteAllText(
            Path.Combine(packDirectory, "pack.yml"),
            string.Join(
                Environment.NewLine,
                [
                    $"id: {packId}",
                    "name: Integration pack",
                    $"version: {version}",
                    "license: MIT",
                    "author: LunaPack integration tests",
                    "description: Verifies the OCI registry lifecycle.",
                    "managedFiles:",
                    "  - source: files/message.txt",
                    "    target: installed/message.txt",
                    "",
                ]
            )
        );
        File.WriteAllText(Path.Combine(filesDirectory, "message.txt"), content);
    }

    private static string ReadManifestDigest(string output)
    {
        var normalizedOutput = NormalizeCliOutput(output);
        var match = Regex.Match(
            normalizedOutput,
            @"manifest(?<digest>sha256:[0-9a-f]{64})",
            RegexOptions.CultureInvariant,
            TimeSpan.FromSeconds(1)
        );
        if (!match.Success)
        {
            throw new InvalidOperationException(
                $"Publication output did not contain a manifest digest: {output}"
            );
        }

        return match.Groups["digest"].Value;
    }

    private static string NormalizeCliOutput(string output) =>
        string.Concat(output.Where(character => !char.IsWhiteSpace(character)));

    private static string NormalizePackColumnOutput(string output)
    {
        var normalized = new StringBuilder();
        foreach (var line in output.Split('\n'))
        {
            var cellStart = line.IndexOf('\u2502');
            if (cellStart < 0)
            {
                continue;
            }

            var cellEnd = line.IndexOf('\u2502', cellStart + 1);
            if (cellEnd < 0)
            {
                continue;
            }

            foreach (var character in line.AsSpan(cellStart + 1, cellEnd - cellStart - 1))
            {
                if (char.IsLetterOrDigit(character) || character == '-')
                {
                    normalized.Append(character);
                }
            }
        }

        return normalized.ToString();
    }

    private static string NormalizePackId(string packId) =>
        string.Concat(
            packId.Where(character => char.IsLetterOrDigit(character) || character == '-')
        );

    private static string GetRepositoryRoot()
    {
        for (
            DirectoryInfo? directory = new(AppContext.BaseDirectory);
            directory is not null;
            directory = directory.Parent
        )
        {
            if (
                File.Exists(Path.Combine(directory.FullName, "global.json"))
                && Directory.Exists(
                    Path.Combine(directory.FullName, "examples", "oci-private-packs")
                )
            )
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException("Unable to find the LunaPack repository root.");
    }

    private static string Describe(CliResult result) =>
        $"{result.StandardOutput}{Environment.NewLine}{result.StandardError}";
}
