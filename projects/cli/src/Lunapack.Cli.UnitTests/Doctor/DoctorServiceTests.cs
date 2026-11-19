using System.IO.Abstractions;
using Lunapack.Cli.Application.CommandExecution;
using Lunapack.Cli.Doctor;
using Lunapack.Cli.Project;
using Lunapack.Cli.Sources;
using Lunapack.Cli.Sources.Git;
using Lunapack.Cli.Trust;

namespace Lunapack.Cli.UnitTests.Doctor;

public sealed class DoctorServiceTests
{
    [Test]
    public async Task Inspect_WhenNoProjectStateExists_ReturnsInformationalEnvironmentReport()
    {
        using var context = new DoctorContext();

        var report = await context.Service.InspectAsync(context.Workspace, checkNetwork: false);

        await Assert.That(report.HasBlockingFailures).IsFalse();
        await Assert.That(Find(report, "Project state").Status).IsEqualTo(DoctorCheckStatus.Info);
        await Assert
            .That(
                context.Git.Arguments.All(arguments =>
                    arguments.SequenceEqual(["--version"], StringComparer.Ordinal)
                )
            )
            .IsTrue();
        await Assert.That(context.Remote.GitProbeCount).IsEqualTo(0);
        await Assert.That(context.Remote.OciProbeCount).IsEqualTo(0);
    }

    [Test]
    public async Task Inspect_WhenGitIsRequiredButUnavailable_ReturnsBlockingFailure()
    {
        using var context = new DoctorContext(gitAvailable: false);
        await context.WriteProjectStateAsync("git");

        var report = await context.Service.InspectAsync(context.Workspace, checkNetwork: false);

        await Assert.That(report.HasBlockingFailures).IsTrue();
        await Assert.That(Find(report, "Git").Status).IsEqualTo(DoctorCheckStatus.Fail);
    }

    [Test]
    public async Task Inspect_WhenGitIsUnavailableButUnneeded_ReturnsInformationalResult()
    {
        using var context = new DoctorContext(gitAvailable: false);
        await context.WriteProjectStateAsync("local");

        var report = await context.Service.InspectAsync(context.Workspace, checkNetwork: false);

        await Assert.That(report.HasBlockingFailures).IsFalse();
        await Assert.That(Find(report, "Git").Status).IsEqualTo(DoctorCheckStatus.Info);
    }

    [Test]
    public async Task Inspect_WhenProjectStateIsInvalid_DoesNotRunRemoteProbes()
    {
        using var context = new DoctorContext();
        File.WriteAllText(
            Path.Combine(context.Workspace, ProjectStateStore.ConfigurationFileName),
            "schemaVersion: 99\n"
        );
        File.WriteAllText(
            Path.Combine(context.Workspace, ProjectStateStore.LockFileName),
            "schemaVersion: 1\npacks: []\n"
        );

        var report = await context.Service.InspectAsync(context.Workspace, checkNetwork: true);

        await Assert.That(report.HasBlockingFailures).IsTrue();
        await Assert.That(Find(report, "Project state").Status).IsEqualTo(DoctorCheckStatus.Fail);
        await Assert.That(context.Remote.GitProbeCount).IsEqualTo(0);
        await Assert.That(context.Remote.OciProbeCount).IsEqualTo(0);
    }

    [Test]
    public async Task Inspect_WhenNetworkIsDisabled_DoesNotProbeConfiguredRemoteSource()
    {
        using var context = new DoctorContext();
        await context.WriteProjectStateAsync("git");

        var report = await context.Service.InspectAsync(context.Workspace, checkNetwork: false);

        await Assert.That(report.HasBlockingFailures).IsFalse();
        await Assert.That(context.Remote.GitProbeCount).IsEqualTo(0);
        await Assert.That(context.Remote.OciProbeCount).IsEqualTo(0);
        await Assert.That(Find(report, "Network").Status).IsEqualTo(DoctorCheckStatus.Info);
    }

    [Test]
    public async Task Inspect_WhenNetworkIsEnabled_ProbesRemoteSourcesOnly()
    {
        using var context = new DoctorContext();
        await context.WriteProjectStateAsync("git", "oci", "local");

        var report = await context.Service.InspectAsync(context.Workspace, checkNetwork: true);

        await Assert.That(report.HasBlockingFailures).IsFalse();
        await Assert.That(context.Remote.GitProbeCount).IsEqualTo(1);
        await Assert.That(context.Remote.OciProbeCount).IsEqualTo(1);
        await Assert
            .That(
                report.Checks.Count(check =>
                    check.Name.StartsWith("Source remote-", StringComparison.Ordinal)
                )
            )
            .IsEqualTo(2);
    }

    [Test]
    public async Task Inspect_WhenRemoteProbeFails_MarksFailureBlocking()
    {
        using var context = new DoctorContext();
        context.Remote.GitResult = new DoctorRemoteProbeResult(
            DoctorRemoteProbeOutcome.Failure,
            "Repository unavailable.",
            "Repair connectivity."
        );
        await context.WriteProjectStateAsync("git");

        var report = await context.Service.InspectAsync(context.Workspace, checkNetwork: true);

        await Assert.That(report.HasBlockingFailures).IsTrue();
        await Assert
            .That(Find(report, "Source remote-git").Status)
            .IsEqualTo(DoctorCheckStatus.Fail);
    }

    [Test]
    public async Task Inspect_WhenSourceProbeExceedsBudget_ReportsTimeoutAndContinues()
    {
        using var context = new DoctorContext(
            remote: new RecordingRemoteSourceProbe { HangGitProbe = true },
            networkSourceTimeout: TimeSpan.FromMilliseconds(20)
        );
        await context.WriteProjectStateAsync("git", "oci");

        var report = await context.Service.InspectAsync(context.Workspace, checkNetwork: true);

        await Assert.That(Find(report, "Source remote-git").Message).Contains("timed out");
        await Assert.That(context.Remote.OciProbeCount).IsEqualTo(1);
    }

    [Test]
    public async Task Inspect_WhenRemoteSourceIsIgnored_DoesNotProbeIt()
    {
        using var context = new DoctorContext();
        await context.WriteProjectStateAsync("git");
        var state = await context.StateStore.LoadAsync(context.Workspace);
        var source = state.RequireValue().Configuration.Sources.Single();
        var ignored = new ConfiguredSourceIgnoreService(context.FileSystem, context.SettingsStore);
        await ignored.SetIgnoredAsync(context.Workspace, source, ignored: true);

        var report = await context.Service.InspectAsync(context.Workspace, checkNetwork: true);

        await Assert.That(context.Remote.GitProbeCount).IsEqualTo(0);
        await Assert
            .That(Find(report, "Source remote-git").Status)
            .IsEqualTo(DoctorCheckStatus.Info);
    }

    [Test]
    public async Task Inspect_WhenProfileCannotBeWritten_ReportsRequiredProbeAsBlocking()
    {
        using var context = new DoctorContext();
        var settingsDirectory = Path.Combine(context.Profile, UserSettingsStore.DirectoryName);
        Directory.CreateDirectory(settingsDirectory);
        context.TemporaryFile.ProbeResult = new DoctorTemporaryFileProbeResult(
            false,
            "Probe failed."
        );
        await context.WriteProjectStateAsync("local");

        var report = await context.Service.InspectAsync(context.Workspace, checkNetwork: false);

        await Assert.That(context.TemporaryFile.ProbedDirectories).Contains(settingsDirectory);
        await Assert
            .That(Find(report, "Profile directory").Status)
            .IsEqualTo(DoctorCheckStatus.Fail);
    }

    [Test]
    public async Task Inspect_WhenProjectStatePathIsLinked_ReportsUnsafeStateAndMakesNoRequest()
    {
        using var context = new DoctorContext();
        var target = Path.Combine(context.Workspace, "external.yml");
        File.WriteAllText(target, "schemaVersion: 1\npacks: []\n");
        var linkedPath = Path.Combine(context.Workspace, ProjectStateStore.LockFileName);
        try
        {
            File.CreateSymbolicLink(linkedPath, target);
        }
        catch (Exception exception)
            when (exception
                    is IOException
                        or UnauthorizedAccessException
                        or PlatformNotSupportedException
            )
        {
            return;
        }

        File.WriteAllText(
            Path.Combine(context.Workspace, ProjectStateStore.ConfigurationFileName),
            "schemaVersion: 1\nsources: []\npacks: []\n"
        );

        var report = await context.Service.InspectAsync(context.Workspace, checkNetwork: true);

        await Assert.That(report.HasBlockingFailures).IsTrue();
        await Assert.That(Find(report, "Project state").Message).Contains("link");
        await Assert.That(context.Remote.GitProbeCount).IsEqualTo(0);
        await Assert.That(context.Remote.OciProbeCount).IsEqualTo(0);
    }

    private static DoctorCheckResult Find(DoctorReport report, string name) =>
        report.Checks.Single(check => string.Equals(check.Name, name, StringComparison.Ordinal));

    private sealed class DoctorContext : IDisposable
    {
        public DoctorContext(
            bool gitAvailable = true,
            RecordingRemoteSourceProbe? remote = null,
            TimeSpan? networkSourceTimeout = null
        )
        {
            Workspace = Path.Combine(
                Path.GetTempPath(),
                "lunapack-doctor-tests",
                Guid.NewGuid().ToString("N")
            );
            Profile = Path.Combine(Workspace, "profile");
            Directory.CreateDirectory(Workspace);
            FileSystem = new FileSystem();
            SettingsStore = new UserSettingsStore(FileSystem, Profile);
            StateStore = new ProjectStateStore(FileSystem);
            Git = new RecordingGitProcessRunner(gitAvailable);
            Remote = remote ?? new RecordingRemoteSourceProbe();
            TemporaryFile = new RecordingTemporaryFileProbe();
            Service = new DoctorService(
                FileSystem,
                StateStore,
                Git,
                () => SettingsStore,
                new StaticCredentialStoreProbe(true),
                TemporaryFile,
                Remote,
                networkSourceTimeout
            );
        }

        public string Workspace { get; }

        public string Profile { get; }

        public RecordingGitProcessRunner Git { get; }

        public RecordingRemoteSourceProbe Remote { get; }

        public RecordingTemporaryFileProbe TemporaryFile { get; }

        public DoctorService Service { get; }

        public IFileSystem FileSystem { get; }

        public ProjectStateStore StateStore { get; }

        public UserSettingsStore SettingsStore { get; }

        public async Task WriteProjectStateAsync(params string[] sourceTypes)
        {
            var sources = sourceTypes.Select(
                (sourceType, index) =>
                    sourceType switch
                    {
                        "git" =>
                            "  - name: remote-git\n    type: git\n    url: https://example.com/packs.git\n",
                        "oci" =>
                            "  - name: remote-oci\n    type: oci\n    url: https://registry.example.com\n",
                        "local" => $"  - name: local-{index}\n    type: local\n    path: packs\n",
                        _ => throw new ArgumentOutOfRangeException(nameof(sourceTypes)),
                    }
            );
            var sourcesYaml =
                sourceTypes.Length == 0 ? "sources: []\n" : $"sources:\n{string.Concat(sources)}";
            File.WriteAllText(
                Path.Combine(Workspace, ProjectStateStore.ConfigurationFileName),
                $"schemaVersion: 1\n{sourcesYaml}packs: []\n"
            );
            File.WriteAllText(
                Path.Combine(Workspace, ProjectStateStore.LockFileName),
                "schemaVersion: 1\npacks: []\n"
            );
            var state = await StateStore.LoadAsync(Workspace);
            await Assert.That(state.IsSuccess).IsTrue();
        }

        public void Dispose()
        {
            if (Directory.Exists(Workspace))
            {
                Directory.Delete(Workspace, recursive: true);
            }
        }
    }

    private sealed class RecordingGitProcessRunner(bool isAvailable) : IGitProcessRunner
    {
        public List<IReadOnlyList<string>> Arguments { get; } = [];

        public Task<ManifestOperationResult<GitProcessOutput>> RunAsync(
            IReadOnlyList<string> arguments,
            TimeSpan timeout,
            CancellationToken cancellationToken
        )
        {
            Arguments.Add([.. arguments]);
            return Task.FromResult(
                isAvailable
                    ? ManifestOperationResult<GitProcessOutput>.Success(
                        new GitProcessOutput("git version 2.0.0", string.Empty)
                    )
                    : ManifestOperationResult<GitProcessOutput>.Failure(
                        "Unable to locate a trusted Git executable."
                    )
            );
        }
    }

    private sealed class RecordingRemoteSourceProbe : IDoctorRemoteSourceProbe
    {
        public int GitProbeCount { get; private set; }

        public int OciProbeCount { get; private set; }

        public DoctorRemoteProbeResult GitResult { get; set; } =
            new(DoctorRemoteProbeOutcome.Success, "Git reachable.");

        public bool HangGitProbe { get; set; }

        public async Task<DoctorRemoteProbeResult> ProbeGitAsync(
            ProjectConfiguration.GitSource source,
            CancellationToken cancellationToken
        )
        {
            GitProbeCount++;
            if (HangGitProbe)
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }

            return GitResult;
        }

        public Task<DoctorRemoteProbeResult> ProbeOciAsync(
            string projectDirectory,
            ProjectConfiguration.OciSource source,
            string? packId,
            CancellationToken cancellationToken
        )
        {
            OciProbeCount++;
            return Task.FromResult(
                new DoctorRemoteProbeResult(DoctorRemoteProbeOutcome.Success, "OCI reachable.")
            );
        }
    }

    private sealed class RecordingTemporaryFileProbe : IDoctorTemporaryFileProbe
    {
        public List<string> ProbedDirectories { get; } = [];

        public DoctorTemporaryFileProbeResult ProbeResult { get; set; } = new(true, "Writable.");

        public DoctorTemporaryFileProbeResult Probe(string directory)
        {
            ProbedDirectories.Add(directory);
            return ProbeResult;
        }
    }

    private sealed class StaticCredentialStoreProbe(bool available) : IDoctorCredentialStoreProbe
    {
        public DoctorCredentialStoreProbeResult Probe() =>
            new(available, "Credential store checked.");
    }
}
