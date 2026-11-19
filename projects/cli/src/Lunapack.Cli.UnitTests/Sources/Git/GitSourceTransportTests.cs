using System.Diagnostics;
using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;
using Lunapack.Cli.Application.CommandExecution;
using Lunapack.Cli.Application.Preferences;
using Lunapack.Cli.Application.Serialization;
using Lunapack.Cli.Catalog;
using Lunapack.Cli.Packs.Manifest;
using Lunapack.Cli.Packs.Planning;
using Lunapack.Cli.Project;
using Lunapack.Cli.Sources;
using Lunapack.Cli.Sources.Git;
using Lunapack.Cli.UnitTests.TestSupport;

namespace Lunapack.Cli.UnitTests.Sources.Git;

public sealed class GitSourceTransportTests
{
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
    public async Task Identity_WhenRepositoryStartsWithOption_RejectsLocation()
    {
        var result = SourceIdentityNormalizer.NormalizeRepository(
            "--upload-pack=sh -c 'sentinel'@example.test:organization/packs.git"
        );

        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That(result.Error).Contains("must not begin");
    }

    [Test]
    public async Task Identity_WhenRepositoryContainsQuery_RejectsLocation()
    {
        var result = SourceIdentityNormalizer.NormalizeRepository(
            "https://example.test/packs.git?token=sentinel"
        );

        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That(result.Error).Contains("queries or fragments");
        await Assert.That(result.Error).DoesNotContain("sentinel");
    }

    [Test]
    public async Task Identity_WhenScpUrlUsesSshAliasCharacters_PreservesRepository()
    {
        var result = SourceIdentityNormalizer.NormalizeRepository(
            "deploy+team@_work-alias.example.:repo.git"
        );

        await Assert.That(result.IsSuccess).IsTrue();
        await Assert.That(result.Value).IsEqualTo("_work-alias.example./repo");
    }

    [Test]
    [Arguments("git@alias-:repo.git", "alias-/repo")]
    [Arguments("git@alias_:repo.git", "alias_/repo")]
    public async Task Identity_WhenScpAliasHasSafeTrailingCharacter_PreservesRepository(
        string repositoryUrl,
        string expectedIdentity
    )
    {
        var result = SourceIdentityNormalizer.NormalizeRepository(repositoryUrl);

        await Assert.That(result.IsSuccess).IsTrue();
        await Assert.That(result.Value).IsEqualTo(expectedIdentity);
    }

    [Test]
    public async Task Identity_WhenInvalidUrlContainsCredentials_DoesNotEchoUrl()
    {
        var result = SourceIdentityNormalizer.NormalizeRepository(
            "https://user:sentinel@example.test"
        );

        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That(result.Error).DoesNotContain("sentinel");
        await Assert.That(result.Error).DoesNotContain("example.test");
    }

    [Test]
    public async Task Identity_WhenHttpsRepositoryContainsBackslash_RejectsLocation()
    {
        var result = SourceIdentityNormalizer.NormalizeRepository(
            @"https://example.test/packs\archive.git"
        );

        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That(result.Error).Contains("must not contain backslashes");
    }

    [Test]
    public async Task Identity_WhenRepositoryUsesRemoteHelper_RejectsLocation()
    {
        var result = SourceIdentityNormalizer.NormalizeRepository("ext::sentinel-command");

        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That(result.Error).Contains("remote-helper");
        await Assert.That(result.Error).DoesNotContain("sentinel");
    }

    [Test]
    [Arguments("//attacker.example/share/repository.git")]
    [Arguments(@"\\attacker.example\share\repository.git")]
    [Arguments(@"/\attacker.example\share\repository.git")]
    [Arguments(@"\/attacker.example/share/repository.git")]
    [Arguments("file://attacker.example/share/repository.git")]
    public async Task Identity_WhenRepositoryUsesHostedFilePath_RejectsLocation(
        string repositoryUrl
    )
    {
        var result = SourceIdentityNormalizer.NormalizeRepository(repositoryUrl);

        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That(result.Error).Contains("UNC");
        await Assert.That(result.Error).DoesNotContain("attacker.example");
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
            TestConsole.Create(),
            TestGitTransportPolicy.Create(fileSystem)
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
            TestConsole.Create(),
            TestGitTransportPolicy.Create(fileSystem)
        );
        using var cancellation = new CancellationTokenSource();

        var result = await discovery.BrowseAsync(
            @"C:\project",
            CreateSource(),
            0,
            cancellation.Token
        );

        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That(processRunner.CancellationTokens.Count).IsEqualTo(2);
        await Assert
            .That(processRunner.CancellationTokens.All(token => token == cancellation.Token))
            .IsTrue();
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
                new PackManifest { Id = "example", Version = "1.0.0" },
                provenance,
                "packs/example"
            ),
        ]);
        var configuration = new ProjectConfiguration { Sources = [source] };
        var materializer = new GitPackMaterializer(
            fileSystem,
            processRunner,
            TestConsole.Create(),
            TestGitTransportPolicy.Create(fileSystem),
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
                processRunner
                    .Calls.Single(call =>
                        call.Arguments.Contains("checkout", StringComparer.Ordinal)
                    )
                    .RepositoryUrl
            )
            .IsEqualTo(source.Url);
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
    public async Task Discovery_WhenBooleanParameterHasDefault_PreservesTypedValue()
    {
        var fileSystem = new MockFileSystem();
        var processRunner = new ParameterManifestGitProcessRunner();
        var discovery = new GitPackDiscovery(
            fileSystem,
            processRunner,
            new GitRefResolver(processRunner),
            new GitSourceCache(fileSystem),
            TestConsole.Create(),
            TestGitTransportPolicy.Create(fileSystem)
        );

        var result = await discovery.BrowseAsync(@"C:\project", CreateSource(), 0);

        await Assert.That(result.IsSuccess).IsTrue().Because(result.Error ?? string.Empty);
        var defaultValue = result.RequireValue().Single().Manifest.Parameters["enabled"].Default;
        await Assert.That(defaultValue).IsTypeOf<bool>();
        await Assert.That((bool)defaultValue!).IsTrue();
    }

    [Test]
    public async Task Discovery_WhenHttpIsEnabled_PropagatesTransportToLazyBlobRead()
    {
        using var workspace = new TestWorkspace();
        var preferences = new CliPreferenceService(
            workspace.FileSystem,
            workspace.StateStore,
            workspace.UserSettingsStore
        );
        await preferences.SetAsync(
            workspace.Path,
            "git.allow-insecure-transport",
            "true",
            CliPreferenceService.Scope.Global
        );
        var processRunner = new ParameterManifestGitProcessRunner();
        var discovery = new GitPackDiscovery(
            workspace.FileSystem,
            processRunner,
            new GitRefResolver(processRunner),
            new GitSourceCache(workspace.FileSystem),
            TestConsole.Create(),
            TestGitTransportPolicy.Create(workspace.FileSystem, workspace.UserSettingsStore)
        );
        var source = CreateSource() with { Url = "http://example.test/packs.git" };

        var result = await discovery.BrowseAsync(workspace.Path, source, 0);
        var show = processRunner.Calls.Single(call =>
            call.Arguments.Contains("show", StringComparer.Ordinal)
        );

        await Assert.That(result.IsSuccess).IsTrue().Because(result.Error ?? string.Empty);
        await Assert.That(show.TransportMode).IsEqualTo(GitTransportMode.AllowHttp);
        await Assert.That(show.RepositoryUrl).IsEqualTo(source.Url);
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
            .IsEquivalentTo([
                "ls-remote",
                "--exit-code",
                "--",
                "https://example.test/packs.git",
                "main",
            ]);
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

    [Test]
    public async Task RefResolver_WhenGitAuthenticationFails_IdentifiesSourceAndIgnoreCommand()
    {
        var resolver = new GitRefResolver(
            new FakeGitProcessRunner(
                ManifestOperationResult<GitProcessOutput>.Failure(
                    "Git exited with code 128: Git authentication failed."
                )
            )
        );

        var result = await resolver.ResolveAsync(
            CreateSource() with
            {
                Name = "engineering",
                Ref = "main",
            },
            null,
            default
        );

        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That(result.Error).Contains("Git source 'engineering'");
        await Assert.That(result.Error).Contains("luna sources ignore engineering");
    }

    [Test]
    public async Task RefResolver_WhenGitOutputIsTruncated_FailsWithoutParsing()
    {
        var resolver = new GitRefResolver(
            new FakeGitProcessRunner(
                ManifestOperationResult<GitProcessOutput>.Success(
                    new GitProcessOutput(
                        $"{Commit}\trefs/heads/main\n",
                        string.Empty,
                        StandardOutputTruncated: true
                    )
                )
            )
        );

        var result = await resolver.ResolveAsync(
            CreateSource() with
            {
                Ref = "main",
            },
            null,
            default
        );

        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That(result.Error).Contains("truncated");
    }

    [Test]
    public async Task RefResolver_WhenRepositoryLooksLikeGitOption_DoesNotRunGit()
    {
        var processRunner = new FakeGitProcessRunner(
            ManifestOperationResult<GitProcessOutput>.Success(
                new GitProcessOutput("unexpected", string.Empty)
            )
        );
        var resolver = new GitRefResolver(processRunner);

        var result = await resolver.ResolveAsync(
            CreateSource() with
            {
                Url = "--upload-pack=sh -c 'sentinel'@example.test:packs.git",
            },
            null,
            default
        );

        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That(processRunner.Arguments).IsEmpty();
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
        public List<GitProcessCall> Calls { get; } = [];

        public Task<ManifestOperationResult<GitProcessOutput>> RunAsync(
            IReadOnlyList<string> arguments,
            TimeSpan timeout,
            CancellationToken cancellationToken
        ) => ExecuteAsync(arguments);

        public Task<ManifestOperationResult<GitProcessOutput>> RunAsync(
            IReadOnlyList<string> arguments,
            TimeSpan timeout,
            GitTransportMode transportMode,
            string? repositoryUrl,
            CancellationToken cancellationToken
        )
        {
            Calls.Add(new GitProcessCall(arguments, transportMode, repositoryUrl));
            return ExecuteAsync(arguments);
        }

        private static Task<ManifestOperationResult<GitProcessOutput>> ExecuteAsync(
            IReadOnlyList<string> arguments
        )
        {
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

    private sealed record GitProcessCall(
        IReadOnlyList<string> Arguments,
        GitTransportMode TransportMode,
        string? RepositoryUrl
    );

    private sealed class CapturingGitProcessRunner(MockFileSystem fileSystem) : IGitProcessRunner
    {
        public List<IReadOnlyList<string>> Arguments { get; } = [];

        public List<GitProcessCall> Calls { get; } = [];

        public Task<ManifestOperationResult<GitProcessOutput>> RunAsync(
            IReadOnlyList<string> arguments,
            TimeSpan timeout,
            CancellationToken cancellationToken
        ) => ExecuteAsync(arguments, fileSystem);

        public Task<ManifestOperationResult<GitProcessOutput>> RunAsync(
            IReadOnlyList<string> arguments,
            TimeSpan timeout,
            GitTransportMode transportMode,
            string? repositoryUrl,
            CancellationToken cancellationToken
        )
        {
            Calls.Add(new GitProcessCall(arguments, transportMode, repositoryUrl));
            return ExecuteAsync(arguments, fileSystem);
        }

        private Task<ManifestOperationResult<GitProcessOutput>> ExecuteAsync(
            IReadOnlyList<string> arguments,
            MockFileSystem fileSystem
        )
        {
            Arguments.Add(arguments);
            if (arguments.Contains("checkout", StringComparer.Ordinal))
            {
                var packDirectory = fileSystem.Path.Combine(arguments[1], "packs", "example");
                fileSystem.AddDirectory(packDirectory);
                fileSystem.AddFile(
                    fileSystem.Path.Combine(packDirectory, "pack.yml"),
                    "id: example\nversion: 1.0.0\n"
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
