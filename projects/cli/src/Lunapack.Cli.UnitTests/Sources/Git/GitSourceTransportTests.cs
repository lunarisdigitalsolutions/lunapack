using System.Diagnostics;
using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;
using System.Text.Json.Nodes;
using Lunapack.Cli.Application.CommandExecution;
using Lunapack.Cli.Application.Preferences;
using Lunapack.Cli.Application.Serialization;
using Lunapack.Cli.Catalog;
using Lunapack.Cli.Packs.Manifest;
using Lunapack.Cli.Packs.Planning;
using Lunapack.Cli.Project;
using Lunapack.Cli.Sources;
using Lunapack.Cli.Sources.Git;

namespace Lunapack.Cli.UnitTests.Sources.Git;

public sealed class GitSourceTransportTests
{
    private static readonly long[] _snapshotAtBoundary = [60L, 40L];
    private static readonly long[] _snapshotOverFileLimit = [60L, 40L, 1L];

    [Test]
    public async Task Identity_WhenGitSourceNeedsNormalization_MatchesCacheIdentity()
    {
        var source = CreateSource() with
        {
            Url = " https://example.test/packs.git/ ",
            Ref = " main ",
            Path = @"/packs\catalog/",
        };

        var identity = ConfiguredSourceIdentity.Create(source);
        var cacheIdentity = GitSourceCacheIdentity.Create(source);

        await Assert.That(identity.Url).IsEqualTo("https://example.test/packs.git");
        await Assert.That(identity.Ref).IsEqualTo("main");
        await Assert.That(identity.Path).IsEqualTo("packs/catalog");
        await Assert.That(cacheIdentity.Url).IsEqualTo(identity.Url);
        await Assert.That(cacheIdentity.Ref).IsEqualTo(identity.Ref);
        await Assert.That(cacheIdentity.Path).IsEqualTo(identity.Path);
    }

    [Test]
    public async Task Cache_WhenEntryMatchesSource_LoadsEntry()
    {
        var fileSystem = new MockFileSystem();
        var cache = new GitSourceCache(fileSystem);
        var source = CreateSource();
        var entry = new GitSourceCacheEntry
        {
            Source = GitSourceCacheIdentity.Create(source),
            ResolvedCommit = Commit,
            DefaultBranch = "main",
        };

        var saved = cache.Save(@"C:\project", entry);
        var loaded = cache.Load(@"C:\project", source);

        await Assert.That(saved.IsSuccess).IsTrue();
        await Assert.That(loaded.IsSuccess).IsTrue();
        await Assert.That(loaded.Value).IsNotNull();
        await Assert.That(loaded.Value?.ResolvedCommit).IsEqualTo(Commit);
    }

    [Test]
    public async Task Cache_WhenParameterHasMultipleDefaults_PreservesDefaults()
    {
        var fileSystem = new MockFileSystem();
        var cache = new GitSourceCache(fileSystem);
        var source = CreateSource();
        var manifest = new PackManifest
        {
            Id = "example",
            Version = "1.0.0",
            Author = "Example Author",
            License = "MIT",
            Parameters = new Dictionary<string, PackManifest.PackParameter>(StringComparer.Ordinal)
            {
                ["features"] = new()
                {
                    Type = "enum",
                    Multiple = true,
                    Default = new List<object> { "api", "docker" },
                    Values = ["api", "docker"],
                },
            },
        };
        var entry = CreateCacheEntry(source, manifest, "packs/example");

        var saved = cache.Save(@"C:\project", entry);
        var loaded = cache.Load(@"C:\project", source);

        await Assert.That(saved.IsSuccess).IsTrue();
        await Assert
            .That(loaded.Value?.Packs.Single().Manifest.Parameters["features"].Default)
            .IsEquivalentTo(new List<object> { "api", "docker" });
    }

    [Test]
    public async Task Cache_WhenParameterHasMultipleDefaults_HasGeneratedJsonMetadata()
    {
        var metadata = LunapackJsonContext.Default.GetTypeInfo(typeof(List<object>));

        await Assert.That(metadata).IsNotNull();
    }

    [Test]
    public async Task Cache_WhenEntryCorrupt_TreatsEntryAsCacheMiss()
    {
        var fileSystem = new MockFileSystem();
        var cache = new GitSourceCache(fileSystem);
        var source = CreateSource();
        var entry = new GitSourceCacheEntry
        {
            Source = GitSourceCacheIdentity.Create(source),
            ResolvedCommit = Commit,
        };
        await Assert.That(cache.Save(@"C:\project", entry).IsSuccess).IsTrue();
        var cacheDirectory = fileSystem.Path.Combine(@"C:\project", ".lunapack", "git-sources");
        var cacheFile = fileSystem.Directory.GetFiles(cacheDirectory).Single();
        fileSystem.File.WriteAllText(cacheFile, "not json");

        var loaded = cache.Load(@"C:\project", source);

        await Assert.That(loaded.IsSuccess).IsTrue();
        await Assert.That(loaded.Value).IsNull();
    }

    [Test]
    public async Task Cache_WhenCachedManifestIsInvalid_TreatsEntryAsCacheMiss()
    {
        var fileSystem = new MockFileSystem();
        var cache = new GitSourceCache(fileSystem);
        var source = CreateSource();
        var manifest = new PackManifest
        {
            Id = "example",
            Version = "1.0.0",
            ManagedFiles =
            [
                new PackManifest.PackManagedFile
                {
                    Path = "template.txt",
                    Target = "../outside.txt",
                },
            ],
        };
        var entry = CreateCacheEntry(source, manifest, "packs/example");
        await Assert.That(cache.Save(@"C:\project", entry).IsSuccess).IsTrue();

        var loaded = cache.Load(@"C:\project", source);

        await Assert.That(loaded.Value).IsNull();
    }

    [Test]
    public async Task Cache_WhenCachedPackPathEscapesRepository_TreatsEntryAsCacheMiss()
    {
        var fileSystem = new MockFileSystem();
        var cache = new GitSourceCache(fileSystem);
        var source = CreateSource();
        var manifest = new PackManifest { Id = "example", Version = "1.0.0" };
        var entry = CreateCacheEntry(source, manifest, "../outside");
        await Assert.That(cache.Save(@"C:\project", entry).IsSuccess).IsTrue();

        var loaded = cache.Load(@"C:\project", source);

        await Assert.That(loaded.Value).IsNull();
    }

    [Test]
    public async Task Cache_WhenCachedPackIdentityDiffersFromManifest_TreatsEntryAsCacheMiss()
    {
        var fileSystem = new MockFileSystem();
        var cache = new GitSourceCache(fileSystem);
        var source = CreateSource();
        var manifest = new PackManifest { Id = "embedded", Version = "1.0.0" };
        var entry = CreateCacheEntry(source, manifest, "packs/example") with
        {
            Packs =
            [
                new GitCachedPack
                {
                    Id = "substituted",
                    Version = manifest.Version,
                    Manifest = manifest,
                    PackPath = "packs/example",
                },
            ],
        };
        await Assert.That(cache.Save(@"C:\project", entry).IsSuccess).IsTrue();

        var loaded = cache.Load(@"C:\project", source);

        await Assert.That(loaded.Value).IsNull();
    }

    [Test]
    public async Task Cache_WhenContentExceedsByteLimit_RejectsReadWithLowerBound()
    {
        var fileSystem = new MockFileSystem();
        var cache = new GitSourceCache(fileSystem);
        var source = CreateSource();
        var entry = CreateCacheEntry(
            source,
            new PackManifest
            {
                Id = "example",
                Version = "1.0.0",
                Author = "Example",
                License = "MIT",
            },
            "packs/example"
        );
        cache.Save(@"C:\project", entry);
        var cachePath = fileSystem
            .Directory.GetFiles(fileSystem.Path.Combine(@"C:\project", ".lunapack", "git-sources"))
            .Single();
        var byteLimit = fileSystem.FileInfo.New(cachePath).Length - 1;

        var loaded = cache.Load(
            @"C:\project",
            source,
            ResourceLimits.Default with
            {
                LockFileBytes = byteLimit,
            }
        );

        await Assert.That(loaded.IsSuccess).IsFalse();
        await Assert
            .That(loaded.ErrorKind)
            .IsEqualTo(ManifestOperationErrorKind.ResourceLimitExceeded);
        await Assert.That(loaded.Error).Contains($"at least {byteLimit + 1} bytes read");
    }

    [Test]
    public async Task Cache_WhenPackRecordCountExceedsLimit_RejectsWrite()
    {
        var fileSystem = new MockFileSystem();
        var cache = new GitSourceCache(fileSystem);
        var source = CreateSource();
        var entry = CreateCacheEntry(
            source,
            new PackManifest
            {
                Id = "example",
                Version = "1.0.0",
                Author = "Example",
                License = "MIT",
            },
            "packs/example"
        );
        entry.Packs.Add(
            new GitCachedPack
            {
                Id = "second",
                Version = "1.0.0",
                Manifest = new PackManifest
                {
                    Id = "second",
                    Version = "1.0.0",
                    Author = "Example",
                    License = "MIT",
                },
                PackPath = "packs/second",
            }
        );

        var saved = cache.Save(
            @"C:\project",
            entry,
            ResourceLimits.Default with
            {
                FilesPerGraph = 1,
            }
        );

        await Assert.That(saved.IsSuccess).IsFalse();
        await Assert
            .That(saved.ErrorKind)
            .IsEqualTo(ManifestOperationErrorKind.ResourceLimitExceeded);
    }

    [Test]
    public async Task Cache_WhenManifestByteCountIsForged_TreatsEntryAsMiss()
    {
        var fileSystem = new MockFileSystem();
        var cache = new GitSourceCache(fileSystem);
        var source = CreateSource();
        var entry = CreateCacheEntry(
            source,
            new PackManifest
            {
                Id = "example",
                Version = "1.0.0",
                Author = "Example",
                License = "MIT",
            },
            "packs/example"
        );
        await Assert.That(cache.Save(@"C:\project", entry).IsSuccess).IsTrue();
        var cacheFile = fileSystem
            .Directory.GetFiles(fileSystem.Path.Combine(@"C:\project", ".lunapack", "git-sources"))
            .Single();
        var cacheJson = JsonNode.Parse(fileSystem.File.ReadAllText(cacheFile));
        cacheJson!["packs"]![0]!["manifestByteCount"] = 1;
        fileSystem.File.WriteAllText(cacheFile, cacheJson.ToJsonString());

        var loaded = cache.Load(@"C:\project", source);

        await Assert.That(loaded.IsSuccess).IsTrue();
        await Assert.That(loaded.Value).IsNull();
    }

    [Test]
    public async Task Cache_WhenAggregateManifestContentExceedsCompiledLimit_RejectsWrite()
    {
        var fileSystem = new MockFileSystem();
        var cache = new GitSourceCache(fileSystem);
        var source = CreateSource();
        var sharedDescription = new string('x', 1_040_000);
        var entry = new GitSourceCacheEntry
        {
            Source = GitSourceCacheIdentity.Create(source),
            ResolvedCommit = Commit,
            Packs =
            [
                .. Enumerable
                    .Range(0, 65)
                    .Select(index => new GitCachedPack
                    {
                        Id = $"pack-{index}",
                        Version = "1.0.0",
                        Manifest = new PackManifest
                        {
                            Id = $"pack-{index}",
                            Version = "1.0.0",
                            Author = "Example",
                            License = "MIT",
                            Description = sharedDescription,
                        },
                        PackPath = $"packs/pack-{index}",
                    }),
            ],
        };

        var saved = cache.Save(@"C:\project", entry);

        await Assert.That(saved.IsSuccess).IsFalse();
        await Assert
            .That(saved.ErrorKind)
            .IsEqualTo(ManifestOperationErrorKind.ResourceLimitExceeded);
        await Assert.That(saved.Error).Contains("byte aggregate limit");
    }

    [Test]
    public async Task Discovery_WhenProjectCacheContainsDifferentManifest_UsesGitCommitData()
    {
        var fileSystem = new MockFileSystem();
        var source = CreateSource();
        var cache = new GitSourceCache(fileSystem);
        var poisonedManifest = new PackManifest
        {
            Id = "example",
            Version = "1.0.0",
            Author = "Attacker",
            License = "MIT",
            Hooks = new PackManifest.PackHooks
            {
                PreInstall =
                [
                    new PackManifest.PackHook { Type = "script", Command = "attacker-tool" },
                ],
            },
        };
        var poisonedEntry = CreateCacheEntry(source, poisonedManifest, "packs/example") with
        {
            DefaultBranch = "attacker-controlled",
        };
        cache.Save(@"C:\project", poisonedEntry);
        var processRunner = new ParameterManifestGitProcessRunner();
        var discovery = new GitPackDiscovery(
            fileSystem,
            processRunner,
            new GitRefResolver(processRunner),
            cache,
            TestConsole.Create()
        );

        var result = await discovery.BrowseAsync(@"C:\project", source, 0);

        await Assert.That(result.IsSuccess).IsTrue().Because(result.Error ?? string.Empty);
        var manifest = result.RequireValue().Single().Manifest;
        await Assert.That(manifest.Hooks).IsNull();
        await Assert.That(manifest.Parameters["enabled"].Default).IsTypeOf<bool>();
        await Assert
            .That(
                processRunner.Calls.First(call =>
                    call[0].Equals("ls-remote", StringComparison.Ordinal)
                )
            )
            .IsEquivalentTo(["ls-remote", "--symref", "https://example.test/packs.git", "HEAD"]);
    }

    [Test]
    public async Task ProcessRunner_WhenCanceledBeforeStart_ReturnsFailure()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var result = await new GitProcessRunner().RunAsync(
            ["--version"],
            TimeSpan.FromSeconds(1),
            cancellation.Token
        );

        await Assert.That(result.IsSuccess).IsFalse();
    }

    [Test]
    public async Task ProcessRunner_WhenExecutableUnavailable_ReturnsFailure()
    {
        var result = await new GitProcessRunner("lunapack-git-does-not-exist").RunAsync(
            ["--version"],
            TimeSpan.FromSeconds(1),
            CancellationToken.None
        );

        await Assert.That(result.IsSuccess).IsFalse();
    }

    [Test]
    public async Task ProcessRunner_WhenGitReturnsNonZero_ReturnsFailure()
    {
        var result = await new GitProcessRunner().RunAsync(
            ["lunapack-command-that-does-not-exist"],
            TimeSpan.FromSeconds(10),
            CancellationToken.None
        );

        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That(result.Error).Contains("Git exited with code");
    }

    [Test]
    public async Task ProcessRunner_WhenPathContainsShellCharacters_PassesLiteralArgument()
    {
        var repositoryPath = Path.Combine(
            Path.GetTempPath(),
            $"lunapack-git-{Guid.NewGuid():N}-&-source"
        );
        try
        {
            var runner = new GitProcessRunner();
            var initialized = await runner.RunAsync(
                ["init", "--quiet", repositoryPath],
                TimeSpan.FromSeconds(10),
                CancellationToken.None
            );
            var result = await runner.RunAsync(
                ["-C", repositoryPath, "rev-parse", "--is-inside-work-tree"],
                TimeSpan.FromSeconds(10),
                CancellationToken.None
            );

            await Assert.That(initialized.IsSuccess).IsTrue();
            await Assert.That(result.IsSuccess).IsTrue();
            await Assert.That(result.RequireValue().StandardOutput.Trim()).IsEqualTo("true");
        }
        finally
        {
            if (Directory.Exists(repositoryPath))
            {
                Directory.Delete(repositoryPath, recursive: true);
            }
        }
    }

    [Test]
    [Retry(3)]
    public async Task ProcessRunner_WhenTimedOut_StopsLongRunningProcess()
    {
        var isWindows = OperatingSystem.IsWindows();
        var executable = isWindows ? "cmd.exe" : "/bin/sh";
        IReadOnlyList<string> arguments = isWindows
            ? ["/c", "ping 127.0.0.1 -n 30 > nul"]
            : ["-c", "sleep 30"];
        var stopwatch = Stopwatch.StartNew();

        var result = await new GitProcessRunner(executable).RunAsync(
            arguments,
            TimeSpan.FromSeconds(1),
            CancellationToken.None
        );

        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That(result.Error).Contains("timed out");
        await Assert.That(stopwatch.Elapsed).IsLessThan(TimeSpan.FromSeconds(10));
    }

    [Test]
    public async Task Discovery_WhenSourcePathEscapesRepository_DoesNotRunGit()
    {
        var fileSystem = new MockFileSystem();
        var processRunner = new FakeGitProcessRunner(
            ManifestOperationResult<GitProcessOutput>.Failure("Git should not run.")
        );
        var discovery = new GitPackDiscovery(
            fileSystem,
            processRunner,
            new GitRefResolver(processRunner),
            new GitSourceCache(fileSystem),
            TestConsole.Create()
        );

        var result = await discovery.BrowseAsync(
            @"C:\project",
            CreateSource() with
            {
                Path = "../packs",
            },
            0
        );

        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That(processRunner.Arguments).IsEmpty();
    }

    [Test]
    public async Task Discovery_WhenCallerProvidesCancellationToken_PropagatesItToGitCommands()
    {
        var fileSystem = new MockFileSystem();
        var processRunner = new CancellationRecordingGitProcessRunner();
        var discovery = new GitPackDiscovery(
            fileSystem,
            processRunner,
            new GitRefResolver(processRunner),
            new GitSourceCache(fileSystem),
            TestConsole.Create()
        );
        using var cancellation = new CancellationTokenSource();

        var result = await discovery.BrowseAsync(
            @"C:\project",
            CreateSource(),
            0,
            cancellationToken: cancellation.Token
        );

        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That(processRunner.CancellationTokens.Count).IsEqualTo(2);
        await Assert
            .That(processRunner.CancellationTokens.All(token => token == cancellation.Token))
            .IsTrue();
    }

    [Test]
    public async Task Discovery_WhenGitPackMatchesSnapshotLimits_AcceptsBoundary()
    {
        var fileSystem = new MockFileSystem();
        var processRunner = new SnapshotListingGitProcessRunner(_snapshotAtBoundary);
        var discovery = new GitPackDiscovery(
            fileSystem,
            processRunner,
            new GitRefResolver(processRunner),
            new GitSourceCache(fileSystem),
            TestConsole.Create()
        );
        var resourceLimits = ResourceLimits.Default with
        {
            FilesPerPack = 2,
            PackSnapshotBytes = 100,
        };

        var result = await discovery.BrowseAsync(
            @"C:\project",
            CreateSource(),
            0,
            resourceLimits: resourceLimits
        );

        await Assert.That(result.IsSuccess).IsTrue().Because(result.Error ?? string.Empty);
    }

    [Test]
    public async Task Discovery_WhenGitPackExceedsFileLimit_RejectsWithLowerBound()
    {
        var fileSystem = new MockFileSystem();
        var processRunner = new SnapshotListingGitProcessRunner(_snapshotOverFileLimit);
        var discovery = new GitPackDiscovery(
            fileSystem,
            processRunner,
            new GitRefResolver(processRunner),
            new GitSourceCache(fileSystem),
            TestConsole.Create()
        );
        var resourceLimits = ResourceLimits.Default with { FilesPerPack = 2 };

        var result = await discovery.BrowseAsync(
            @"C:\project",
            CreateSource(),
            0,
            resourceLimits: resourceLimits
        );

        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That(result.Error).Contains("2 record limit");
        await Assert.That(result.Error).Contains("at least 3 records read");
    }

    [Test]
    public async Task Discovery_WhenGitPackParentDirectoriesExceedLimit_RejectsBeforeCheckout()
    {
        var fileSystem = new MockFileSystem();
        var processRunner = new SnapshotListingGitProcessRunner([60L, 40L], nested: true);
        var discovery = new GitPackDiscovery(
            fileSystem,
            processRunner,
            new GitRefResolver(processRunner),
            new GitSourceCache(fileSystem),
            TestConsole.Create()
        );
        var resourceLimits = ResourceLimits.Default with { FilesPerPack = 2 };

        var result = await discovery.BrowseAsync(
            @"C:\project",
            CreateSource(),
            0,
            resourceLimits: resourceLimits
        );

        await Assert.That(result.IsSuccess).IsFalse();
        await Assert
            .That(result.ErrorKind)
            .IsEqualTo(ManifestOperationErrorKind.ResourceLimitExceeded);
        await Assert.That(result.Error).Contains("file and directory limit");
    }

    [Test]
    public async Task Discovery_WhenGitPackExceedsSnapshotBytes_RejectsAtLimit()
    {
        var fileSystem = new MockFileSystem();
        var processRunner = new SnapshotListingGitProcessRunner(_snapshotAtBoundary);
        var discovery = new GitPackDiscovery(
            fileSystem,
            processRunner,
            new GitRefResolver(processRunner),
            new GitSourceCache(fileSystem),
            TestConsole.Create()
        );
        var resourceLimits = ResourceLimits.Default with { PackSnapshotBytes = 99 };

        var result = await discovery.BrowseAsync(
            @"C:\project",
            CreateSource(),
            0,
            resourceLimits: resourceLimits
        );

        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That(result.Error).Contains("99 byte limit");
        await Assert.That(result.Error).Contains("at least 100 bytes observed");
    }

    [Test]
    public async Task Materializer_WhenGitPackSelected_UsesShallowFilteredSparseCheckout()
    {
        var fileSystem = new MockFileSystem();
        var processRunner = new CapturingGitProcessRunner(fileSystem);
        var source = CreateSource() with { Ref = "main", Path = "packs" };
        var provenance = new GitSourceProvenance
        {
            Url = source.Url,
            Ref = source.Ref,
            Path = source.Path,
            ResolvedCommit = Commit,
        };
        var graph = new ResolvedPackGraph([
            new DiscoveredPack(
                "source",
                "packs/example",
                new PackManifest
                {
                    Id = "example",
                    Version = "1.0.0",
                    Author = "Example",
                    License = "MIT",
                },
                provenance,
                "packs/example"
            ),
        ]);
        var configuration = new ProjectConfiguration { Sources = [source] };
        var materializer = new GitPackMaterializer(
            fileSystem,
            processRunner,
            TestConsole.Create(),
            new NoOpOperationSnapshotSecurity()
        );

        var result = await materializer.MaterializeAsync(@"C:\project", graph, configuration);
        await using var materialization = result.RequireValue();

        await Assert.That(result.IsSuccess).IsTrue();
        await Assert
            .That(
                processRunner.Arguments.Any(arguments =>
                    arguments.Contains("fetch", StringComparer.Ordinal)
                    && arguments.Contains("--depth=1", StringComparer.Ordinal)
                    && arguments.Contains("--filter=blob:none", StringComparer.Ordinal)
                )
            )
            .IsTrue();
        await Assert
            .That(
                processRunner.Arguments.Any(arguments =>
                    arguments.Contains("sparse-checkout", StringComparer.Ordinal)
                    && arguments.Contains("set", StringComparer.Ordinal)
                    && arguments.Contains("/packs/example/**", StringComparer.Ordinal)
                )
            )
            .IsTrue();
    }

    [Test]
    public async Task Materializer_WhenCheckoutManifestDiffersFromResolvedManifest_RejectsPack()
    {
        var fileSystem = new MockFileSystem();
        var processRunner = new CapturingGitProcessRunner(fileSystem);
        var source = CreateSource() with { Ref = "main", Path = "packs" };
        var provenance = new GitSourceProvenance
        {
            Url = source.Url,
            Ref = source.Ref,
            Path = source.Path,
            ResolvedCommit = Commit,
        };
        var graph = new ResolvedPackGraph([
            new DiscoveredPack(
                "source",
                "packs/example",
                new PackManifest
                {
                    Id = "example",
                    Version = "1.0.0",
                    Author = "Example",
                    License = "MIT",
                    Hooks = new PackManifest.PackHooks
                    {
                        PreInstall =
                        [
                            new PackManifest.PackHook { Type = "script", Command = "substituted" },
                        ],
                    },
                },
                provenance,
                "packs/example"
            ),
        ]);
        var materializer = new GitPackMaterializer(
            fileSystem,
            processRunner,
            TestConsole.Create(),
            new NoOpOperationSnapshotSecurity()
        );

        var result = await materializer.MaterializeAsync(
            @"C:\project",
            graph,
            new ProjectConfiguration { Sources = [source] }
        );

        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That(result.Error).Contains("does not match the manifest resolved");
    }

    [Test]
    public async Task ResolveLocked_WhenGitRefMoves_ReadsPackFromLockedCommit()
    {
        var fileSystem = new MockFileSystem();
        var processRunner = new LockedCommitGitProcessRunner();
        var source = CreateSource() with { Ref = "main", Path = "packs" };
        var provenance = new GitSourceProvenance
        {
            Url = source.Url,
            Ref = source.Ref,
            Path = source.Path,
            ResolvedCommit = Commit,
        };
        var lockedPack = new ProjectLockFile.ResolvedPack
        {
            Id = "example",
            Version = "1.0.0",
            SourceName = source.Name,
            SourceIdentity = ConfiguredSourceIdentity.Create(source),
            GitSource = provenance,
            PackPath = "packs/example",
        };
        using var catalog = new PackCatalog(
            fileSystem,
            TestConsole.Create(),
            processRunner: processRunner
        );

        var result = await catalog.ResolveLockedAsync(
            @"C:\project",
            new ProjectConfiguration { Sources = [source] },
            lockedPack
        );

        await Assert.That(result.IsSuccess).IsTrue().Because(result.Error ?? string.Empty);
        await Assert.That(processRunner.FetchedCommit).IsEqualTo(Commit);
        await Assert
            .That(
                processRunner.Calls.Any(arguments =>
                    arguments.Contains("ls-remote", StringComparer.Ordinal)
                )
            )
            .IsFalse();
    }

    [Test]
    public async Task Discovery_WhenBooleanParameterHasDefault_PreservesTypedValue()
    {
        var fileSystem = new MockFileSystem();
        var processRunner = new ParameterManifestGitProcessRunner();
        var discovery = new GitPackDiscovery(
            fileSystem,
            processRunner,
            new GitRefResolver(processRunner),
            new GitSourceCache(fileSystem),
            TestConsole.Create()
        );

        var result = await discovery.BrowseAsync(@"C:\project", CreateSource(), 0);

        await Assert.That(result.IsSuccess).IsTrue().Because(result.Error ?? string.Empty);
        var defaultValue = result.RequireValue().Single().Manifest.Parameters["enabled"].Default;
        await Assert.That(defaultValue).IsTypeOf<bool>();
        await Assert.That((bool)defaultValue!).IsTrue();
    }

    [Test]
    public async Task RefResolver_WhenCachedDefaultBranchResolves_ReusesCachedBranch()
    {
        var processRunner = new FakeGitProcessRunner(
            ManifestOperationResult<GitProcessOutput>.Success(
                new GitProcessOutput($"{Commit}\trefs/heads/main\n", string.Empty)
            )
        );
        var resolver = new GitRefResolver(processRunner);

        var result = await resolver.ResolveAsync(
            CreateSource() with
            {
                Ref = null,
            },
            "main",
            default
        );

        await Assert.That(result.RequireValue().ResolvedCommit).IsEqualTo(Commit);
        await Assert.That(result.RequireValue().DefaultBranch).IsEqualTo("main");
        await Assert
            .That(processRunner.Arguments)
            .IsEquivalentTo(["ls-remote", "--exit-code", "https://example.test/packs.git", "main"]);
    }

    [Test]
    public async Task RefResolver_WhenRemoteHeadResolved_ParsesDefaultBranchAndCommit()
    {
        var resolver = new GitRefResolver(
            new FakeGitProcessRunner(
                ManifestOperationResult<GitProcessOutput>.Success(
                    new GitProcessOutput(
                        $"ref: refs/heads/main\tHEAD\n{Commit}\tHEAD\n",
                        string.Empty
                    )
                )
            )
        );

        var result = await resolver.ResolveAsync(CreateSource() with { Ref = null }, null, default);

        await Assert.That(result.RequireValue().ResolvedCommit).IsEqualTo(Commit);
        await Assert.That(result.RequireValue().DefaultBranch).IsEqualTo("main");
    }

    private const string Commit = "0123456789abcdef0123456789abcdef01234567";

    private static ProjectConfiguration.GitSource CreateSource() =>
        new() { Name = "git", Url = "https://example.test/packs.git" };

    private static GitSourceCacheEntry CreateCacheEntry(
        ProjectConfiguration.GitSource source,
        PackManifest manifest,
        string packPath
    ) =>
        new()
        {
            Source = GitSourceCacheIdentity.Create(source),
            ResolvedCommit = Commit,
            Packs =
            [
                new GitCachedPack
                {
                    Id = manifest.Id,
                    Version = manifest.Version,
                    Manifest = manifest,
                    ManifestByteCount = 1,
                    PackPath = packPath,
                },
            ],
        };

    private sealed class FakeGitProcessRunner(ManifestOperationResult<GitProcessOutput> result)
        : IGitProcessRunner
    {
        public IReadOnlyList<string> Arguments { get; private set; } = [];

        public Task<ManifestOperationResult<GitProcessOutput>> RunAsync(
            IReadOnlyList<string> arguments,
            TimeSpan timeout,
            CancellationToken cancellationToken
        )
        {
            Arguments = arguments;
            return Task.FromResult(result);
        }
    }

    private sealed class CancellationRecordingGitProcessRunner : IGitProcessRunner
    {
        public List<CancellationToken> CancellationTokens { get; } = [];

        public Task<ManifestOperationResult<GitProcessOutput>> RunAsync(
            IReadOnlyList<string> arguments,
            TimeSpan timeout,
            CancellationToken cancellationToken
        )
        {
            CancellationTokens.Add(cancellationToken);
            return Task.FromResult(
                string.Equals(arguments[0], "ls-remote", StringComparison.Ordinal)
                    ? ManifestOperationResult<GitProcessOutput>.Success(
                        new GitProcessOutput(
                            $"ref: refs/heads/main\tHEAD\n{Commit}\tHEAD\n",
                            string.Empty
                        )
                    )
                    : ManifestOperationResult<GitProcessOutput>.Failure("Stop after resolution.")
            );
        }
    }

    private sealed class ParameterManifestGitProcessRunner : IGitProcessRunner
    {
        public List<IReadOnlyList<string>> Calls { get; } = [];

        public Task<ManifestOperationResult<GitProcessOutput>> RunAsync(
            IReadOnlyList<string> arguments,
            TimeSpan timeout,
            CancellationToken cancellationToken
        )
        {
            Calls.Add(arguments);
            var output =
                string.Equals(arguments[0], "ls-remote", StringComparison.Ordinal)
                    ? $"ref: refs/heads/main\tHEAD\n{Commit}\tHEAD\n"
                : arguments.Contains("ls-tree", StringComparer.Ordinal) ? "packs/example/pack.yml\n"
                : arguments.Contains("show", StringComparer.Ordinal)
                    ? "id: example\nversion: 1.0.0\nauthor: Example\nlicense: MIT\nparameters:\n  enabled:\n    type: bool\n    default: true\n"
                : string.Empty;
            return Task.FromResult(
                ManifestOperationResult<GitProcessOutput>.Success(
                    new GitProcessOutput(output, string.Empty)
                )
            );
        }
    }

    private sealed class SnapshotListingGitProcessRunner(
        IReadOnlyList<long> fileSizes,
        bool nested = false
    ) : IGitProcessRunner
    {
        public Task<ManifestOperationResult<GitProcessOutput>> RunAsync(
            IReadOnlyList<string> arguments,
            TimeSpan timeout,
            CancellationToken cancellationToken
        )
        {
            var output =
                string.Equals(arguments[0], "ls-remote", StringComparison.Ordinal)
                    ? $"ref: refs/heads/main\tHEAD\n{Commit}\tHEAD\n"
                : arguments.Contains("show", StringComparer.Ordinal)
                    ? "id: example\nversion: 1.0.0\nauthor: Example\nlicense: MIT\n"
                : arguments.Contains("ls-tree", StringComparer.Ordinal)
                && arguments.Contains("--name-only", StringComparer.Ordinal)
                    ? "packs/example/pack.yml\0"
                : arguments.Contains("ls-tree", StringComparer.Ordinal)
                    ? string.Concat(
                        fileSizes.Select(
                            (size, index) =>
                                $"100644 blob 0123456789abcdef0123456789abcdef01234567 {size}\tpacks/example/{(index == 0 ? "pack.yml" : nested ? $"directory-{index}/file.txt" : $"file-{index}.txt")}\0"
                        )
                    )
                : string.Empty;
            return Task.FromResult(
                ManifestOperationResult<GitProcessOutput>.Success(
                    new GitProcessOutput(output, string.Empty)
                )
            );
        }
    }

    private sealed class LockedCommitGitProcessRunner : IGitProcessRunner
    {
        public List<IReadOnlyList<string>> Calls { get; } = [];

        public string? FetchedCommit { get; private set; }

        public Task<ManifestOperationResult<GitProcessOutput>> RunAsync(
            IReadOnlyList<string> arguments,
            TimeSpan timeout,
            CancellationToken cancellationToken
        )
        {
            Calls.Add(arguments);
            if (arguments.Contains("fetch", StringComparer.Ordinal))
            {
                FetchedCommit = arguments[^1];
            }

            var output =
                arguments.Contains("ls-tree", StringComparer.Ordinal)
                && arguments.Contains("--name-only", StringComparer.Ordinal)
                    ? "packs/example/pack.yml\0"
                : arguments.Contains("ls-tree", StringComparer.Ordinal)
                    ? "100644 blob 0123456789abcdef0123456789abcdef01234567 85\tpacks/example/pack.yml\0"
                : arguments.Contains("show", StringComparer.Ordinal)
                    ? "id: example\nversion: 1.0.0\nauthor: Example\nlicense: MIT\n"
                : string.Empty;
            return Task.FromResult(
                ManifestOperationResult<GitProcessOutput>.Success(
                    new GitProcessOutput(output, string.Empty)
                )
            );
        }
    }

    private sealed class CapturingGitProcessRunner(MockFileSystem fileSystem) : IGitProcessRunner
    {
        public List<IReadOnlyList<string>> Arguments { get; } = [];

        public Task<ManifestOperationResult<GitProcessOutput>> RunAsync(
            IReadOnlyList<string> arguments,
            TimeSpan timeout,
            CancellationToken cancellationToken
        )
        {
            Arguments.Add(arguments);
            if (arguments.Contains("checkout", StringComparer.Ordinal))
            {
                var packDirectory = fileSystem.Path.Combine(arguments[1], "packs", "example");
                fileSystem.AddDirectory(packDirectory);
                fileSystem.AddFile(
                    fileSystem.Path.Combine(packDirectory, "pack.yml"),
                    "id: example\nversion: 1.0.0\nauthor: Example\nlicense: MIT\n"
                );
            }
            return Task.FromResult(
                ManifestOperationResult<GitProcessOutput>.Success(new GitProcessOutput("", ""))
            );
        }
    }

    private sealed class NoOpOperationSnapshotSecurity : IOperationSnapshotSecurity
    {
        public void ApplyDirectory(string path) { }

        public void ApplyFile(string path) { }

        public void MakeReadOnly(IFileSystem fileSystem, string root) { }

        public void PrepareForDelete(IFileSystem fileSystem, string root) { }
    }
}
