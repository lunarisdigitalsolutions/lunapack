using System.IO.Abstractions.TestingHelpers;
using System.Text;

namespace Lunapack.Cli.UnitTests;

public sealed class GitLinkSourceTests
{
    private const string Commit = "0123456789abcdef0123456789abcdef01234567";
    private const string AgentBlobId = "9f2f8e0b0e4b8d1f5f2b1a0c3d4e5f60718293a4";

    [Test]
    [Arguments("Windows", null, new[] { "LunaPack", "cache", "sources" })]
    [Arguments("MacOs", null, new[] { "Library", "Caches", "LunaPack", "sources" })]
    [Arguments("Linux", null, new[] { ".cache", "lunapack", "sources" })]
    public async Task Resolve_WhenPlatformIsKnown_UsesUserCacheLayout(
        string platformName,
        string? xdgCacheHome,
        string[] expectedSegments
    )
    {
        var fileSystem = new MockFileSystem();
        var platform = Enum.Parse<LinkCachePlatform>(platformName);
        var root =
            platform is LinkCachePlatform.Windows ? @"C:\Users\luna\AppData\Local" : "/home/luna";

        var cacheRoot = LinkSourceCacheRoot.Resolve(
            fileSystem,
            platform,
            @"C:\Users\luna\AppData\Local",
            "/home/luna",
            xdgCacheHome
        );

        await Assert.That(cacheRoot).IsEqualTo(Path.Combine([root, .. expectedSegments]));
    }

    [Test]
    public async Task Resolve_WhenXdgCacheHomeIsSet_UsesIt()
    {
        var cacheRoot = LinkSourceCacheRoot.Resolve(
            new MockFileSystem(),
            LinkCachePlatform.Linux,
            @"C:\Users\luna\AppData\Local",
            "/home/luna",
            "/cache"
        );

        await Assert.That(cacheRoot).IsEqualTo(Path.Combine("/cache", "lunapack", "sources"));
    }

    [Test]
    public async Task ParseTree_WhenTreeContainsIrregularEntries_KeepsRegularBlobsOnly()
    {
        var output = string.Join(
            '\0',
            $"100644 blob {AgentBlobId}\tagents/expert.agent.md",
            $"100755 blob {AgentBlobId}\tagents/run.sh",
            $"120000 blob {AgentBlobId}\tagents/link.md",
            $"160000 commit {Commit}\tvendor/module",
            $"040000 tree {Commit}\tagents"
        );

        var tree = GitLinkSourceProvider.ParseTree(output, null);

        await Assert
            .That(tree.Select(entry => entry.Path))
            .IsEquivalentTo(["agents/expert.agent.md", "agents/run.sh"]);
    }

    [Test]
    public async Task ParseTree_WhenSourceDeclaresPath_ScopesAndStripsIt()
    {
        var output = string.Join(
            '\0',
            $"100644 blob {AgentBlobId}\tcatalog/agents/expert.agent.md",
            $"100644 blob {AgentBlobId}\tREADME.md"
        );

        var tree = GitLinkSourceProvider.ParseTree(output, "catalog");

        await Assert
            .That(tree.Select(entry => entry.Path))
            .IsEquivalentTo(["agents/expert.agent.md"]);
    }

    [Test]
    public async Task ListAsync_WhenLinkOverridesRef_ResolvesThatRef()
    {
        var runner = new ScriptedGitProcessRunner();
        var provider = CreateProvider(new MockFileSystem(), runner);

        var listing = await provider.ListAsync(
            "/project",
            CreateSource(),
            CreateLink(reference: "v2"),
            null,
            CancellationToken.None
        );

        await Assert.That(listing.RequireValue().GitSource?.Ref).IsEqualTo("v2");
        await Assert.That(runner.Invocations[0]).Contains("v2");
        listing.RequireValue().Dispose();
    }

    [Test]
    public async Task ListAsync_WhenLinkOmitsRef_InheritsTheConfiguredSourceRef()
    {
        var runner = new ScriptedGitProcessRunner();
        var provider = CreateProvider(new MockFileSystem(), runner);

        var listing = await provider.ListAsync(
            "/project",
            CreateSource(),
            CreateLink(),
            null,
            CancellationToken.None
        );

        await Assert.That(listing.RequireValue().GitSource?.Ref).IsEqualTo("main");
        await Assert.That(runner.Invocations[0]).Contains("main");
        listing.RequireValue().Dispose();
    }

    [Test]
    public async Task ListAsync_WhenRefCannotBeResolved_Fails()
    {
        var runner = new ScriptedGitProcessRunner { ResolveRefs = false };
        var provider = CreateProvider(new MockFileSystem(), runner);

        var listing = await provider.ListAsync(
            "/project",
            CreateSource(),
            CreateLink(),
            null,
            CancellationToken.None
        );

        await Assert.That(listing.IsSuccess).IsFalse();
    }

    [Test]
    public async Task ListAsync_WhenCommitIsCached_ReusesCachedTreeWithoutFetching()
    {
        var fileSystem = new MockFileSystem();
        var cache = new GitLinkCache(fileSystem, "/cache/sources");
        cache.SaveMetadata(
            new GitLinkCacheMetadata
            {
                ResolvedCommit = Commit,
                Source = ConfiguredSourceIdentity.Create(CreateSource()),
                Tree = [new GitLinkCacheEntry { BlobId = AgentBlobId, Path = "agents/a.md" }],
            }
        );
        var runner = new ScriptedGitProcessRunner();
        var provider = new GitLinkSourceProvider(
            fileSystem,
            runner,
            new GitRefResolver(runner),
            cache
        );

        using var listing = (
            await provider.ListAsync(
                "/project",
                CreateSource(),
                CreateLink(),
                null,
                CancellationToken.None
            )
        ).RequireValue();

        await Assert.That(listing.Paths).IsEquivalentTo(["agents/a.md"]);
        await Assert.That(runner.Invocations.Count).IsEqualTo(1);
    }

    [Test]
    public async Task MaterializeAsync_WhenBlobIsCached_ReadsCachedBytes()
    {
        var fileSystem = new MockFileSystem();
        var cache = new GitLinkCache(fileSystem, "/cache/sources");
        var contents = Encoding.UTF8.GetBytes("agent");
        var blobId = GitObjectId.ComputeBlobId(contents, 40);
        var identity = ConfiguredSourceIdentity.Create(CreateSource());
        cache.SaveBlob(identity, Commit, blobId, contents);
        cache.SaveMetadata(
            new GitLinkCacheMetadata
            {
                ResolvedCommit = Commit,
                Source = identity,
                Tree = [new GitLinkCacheEntry { BlobId = blobId, Path = "agents/a.md" }],
            }
        );
        var runner = new ScriptedGitProcessRunner();
        var provider = new GitLinkSourceProvider(
            fileSystem,
            runner,
            new GitRefResolver(runner),
            cache
        );
        using var listing = (
            await provider.ListAsync(
                "/project",
                CreateSource(),
                CreateLink(),
                null,
                CancellationToken.None
            )
        ).RequireValue();
        using var workspace = LinkOperationWorkspace.Create(fileSystem);

        var snapshots = (
            await provider.MaterializeAsync(
                listing,
                ["agents/a.md"],
                workspace,
                CancellationToken.None
            )
        ).RequireValue();

        await Assert
            .That(Encoding.UTF8.GetString(workspace.Read(snapshots["agents/a.md"])))
            .IsEqualTo("agent");
        await Assert.That(runner.Invocations.Count).IsEqualTo(1);
    }

    [Test]
    public async Task TryReadBlob_WhenCachedBytesAreTampered_DiscardsTheEntry()
    {
        var fileSystem = new MockFileSystem();
        var cache = new GitLinkCache(fileSystem, "/cache/sources");
        var identity = ConfiguredSourceIdentity.Create(CreateSource());
        var contents = Encoding.UTF8.GetBytes("agent");
        var blobId = GitObjectId.ComputeBlobId(contents, 40);
        cache.SaveBlob(identity, Commit, blobId, Encoding.UTF8.GetBytes("tampered"));

        await Assert.That(cache.TryReadBlob(identity, Commit, blobId)).IsNull();
        await Assert.That(cache.TryReadBlob(identity, Commit, blobId)).IsNull();
    }

    [Test]
    public async Task LoadMetadata_WhenCommitDiffers_IsolatesCachedCommits()
    {
        var fileSystem = new MockFileSystem();
        var cache = new GitLinkCache(fileSystem, "/cache/sources");
        var identity = ConfiguredSourceIdentity.Create(CreateSource());
        cache.SaveMetadata(
            new GitLinkCacheMetadata
            {
                ResolvedCommit = Commit,
                Source = identity,
                Tree = [new GitLinkCacheEntry { BlobId = AgentBlobId, Path = "agents/a.md" }],
            }
        );
        var otherCommit = Commit.Replace("0", "9", StringComparison.Ordinal);
        cache.SaveMetadata(
            new GitLinkCacheMetadata
            {
                ResolvedCommit = otherCommit,
                Source = identity,
                Tree = [new GitLinkCacheEntry { BlobId = AgentBlobId, Path = "agents/b.md" }],
            }
        );

        await Assert
            .That(cache.LoadMetadata(identity, Commit)?.Tree.Single().Path)
            .IsEqualTo("agents/a.md");
        await Assert
            .That(cache.LoadMetadata(identity, otherCommit)?.Tree.Single().Path)
            .IsEqualTo("agents/b.md");
    }

    [Test]
    public async Task LoadMetadata_WhenMetadataIsCorrupt_TreatsEntryAsMiss()
    {
        var fileSystem = new MockFileSystem();
        var cache = new GitLinkCache(fileSystem, "/cache/sources");
        var identity = ConfiguredSourceIdentity.Create(CreateSource());
        cache.SaveMetadata(
            new GitLinkCacheMetadata
            {
                ResolvedCommit = Commit,
                Source = identity,
                Tree = [new GitLinkCacheEntry { BlobId = AgentBlobId, Path = "agents/a.md" }],
            }
        );
        var metadataPath = fileSystem
            .Directory.EnumerateFiles(
                "/cache/sources",
                "metadata.json",
                SearchOption.AllDirectories
            )
            .Single();
        fileSystem.File.WriteAllText(metadataPath, "{ not json");

        await Assert.That(cache.LoadMetadata(identity, Commit)).IsNull();
    }

    [Test]
    public async Task ListAsync_WhenTreeIsFetched_DoesNotRequireAPackManifest()
    {
        var fileSystem = new MockFileSystem();
        var runner = new ScriptedGitProcessRunner
        {
            TreeOutput = string.Join(
                '\0',
                $"100644 blob {AgentBlobId}\tagents/expert.agent.md",
                $"100644 blob {AgentBlobId}\tREADME.md"
            ),
        };
        var provider = CreateProvider(fileSystem, runner);

        using var listing = (
            await provider.ListAsync(
                "/project",
                CreateSource(),
                CreateLink(),
                null,
                CancellationToken.None
            )
        ).RequireValue();

        await Assert.That(listing.Paths).IsEquivalentTo(["agents/expert.agent.md", "README.md"]);
        await Assert
            .That(
                runner.Invocations.Any(arguments =>
                    arguments.Contains("ls-tree", StringComparer.Ordinal)
                )
            )
            .IsTrue();
    }

    [Test]
    public async Task ListAsync_WhenCancellationIsRequested_PropagatesTheToken()
    {
        var runner = new ScriptedGitProcessRunner();
        var provider = CreateProvider(new MockFileSystem(), runner);
        using var cancellation = new CancellationTokenSource();

        var listing = await provider.ListAsync(
            "/project",
            CreateSource(),
            CreateLink(),
            null,
            cancellation.Token
        );

        listing.Value?.Dispose();
        await Assert
            .That(runner.CancellationTokens.All(token => token == cancellation.Token))
            .IsTrue();
    }

    [Test]
    public async Task ListAsync_WhenSourceDeclaresTimeout_UsesIt()
    {
        var runner = new ScriptedGitProcessRunner();
        var provider = CreateProvider(new MockFileSystem(), runner);
        var source = CreateSource() with { TimeoutSeconds = 11 };

        var listing = await provider.ListAsync(
            "/project",
            source,
            CreateLink(),
            null,
            CancellationToken.None
        );

        listing.Value?.Dispose();
        await Assert.That(runner.Timeouts).Contains(TimeSpan.FromSeconds(11));
    }

    [Test]
    public async Task ListAsync_WhenRepositoryFetchFails_RemovesTheOperationRepository()
    {
        var fileSystem = new MockFileSystem();
        var runner = new ScriptedGitProcessRunner { FetchSucceeds = false };
        var provider = CreateProvider(fileSystem, runner);

        var listing = await provider.ListAsync(
            "/project",
            CreateSource(),
            CreateLink(),
            null,
            CancellationToken.None
        );

        await Assert.That(listing.IsSuccess).IsFalse();
        await Assert
            .That(
                fileSystem.Directory.Exists(
                    fileSystem.Path.Combine(fileSystem.Path.GetTempPath(), "lunapack", "links")
                )
            )
            .IsFalse();
    }

    private static GitLinkSourceProvider CreateProvider(
        MockFileSystem fileSystem,
        ScriptedGitProcessRunner runner
    ) =>
        new(
            fileSystem,
            runner,
            new GitRefResolver(runner),
            new GitLinkCache(fileSystem, "/cache/sources")
        );

    private static ProjectConfiguration.GitSource CreateSource() =>
        new()
        {
            Name = "awesome-copilot",
            Ref = "main",
            Url = "https://example.test/awesome-copilot.git",
        };

    private static ProjectConfiguration.Link CreateLink(string? reference = null) =>
        new()
        {
            Includes = ["**/*.md"],
            Ref = reference,
            Source = "awesome-copilot",
            Target = ".github/agents",
        };

    private sealed class ScriptedGitProcessRunner : IGitProcessRunner
    {
        public List<CancellationToken> CancellationTokens { get; } = [];

        public bool FetchSucceeds { get; init; } = true;

        public List<IReadOnlyList<string>> Invocations { get; } = [];

        public bool ResolveRefs { get; init; } = true;

        public List<TimeSpan> Timeouts { get; } = [];

        public string TreeOutput { get; init; } = $"100644 blob {AgentBlobId}\tagents/a.md";

        public Task<ManifestOperationResult<GitProcessOutput>> RunAsync(
            IReadOnlyList<string> arguments,
            TimeSpan timeout,
            CancellationToken cancellationToken
        )
        {
            Invocations.Add(arguments);
            Timeouts.Add(timeout);
            CancellationTokens.Add(cancellationToken);

            if (arguments.Contains("ls-remote", StringComparer.Ordinal))
            {
                return Task.FromResult(
                    ResolveRefs
                        ? ManifestOperationResult<GitProcessOutput>.Success(
                            new GitProcessOutput($"{Commit}\trefs/heads/main\n", string.Empty)
                        )
                        : ManifestOperationResult<GitProcessOutput>.Failure("Unknown ref.")
                );
            }

            if (arguments.Contains("fetch", StringComparer.Ordinal) && !FetchSucceeds)
            {
                return Task.FromResult(
                    ManifestOperationResult<GitProcessOutput>.Failure("Fetch failed.")
                );
            }

            return Task.FromResult(
                ManifestOperationResult<GitProcessOutput>.Success(
                    new GitProcessOutput(
                        arguments.Contains("ls-tree", StringComparer.Ordinal)
                            ? TreeOutput
                            : string.Empty,
                        string.Empty
                    )
                )
            );
        }
    }
}
