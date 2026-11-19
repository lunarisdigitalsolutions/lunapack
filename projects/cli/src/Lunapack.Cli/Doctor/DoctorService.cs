using System.IO.Abstractions;
using Lunapack.Cli.Links;
using Lunapack.Cli.Project;
using Lunapack.Cli.Sources;
using Lunapack.Cli.Sources.Git;
using Lunapack.Cli.Trust;

namespace Lunapack.Cli.Doctor;

internal sealed class DoctorService(
    IFileSystem fileSystem,
    ProjectStateStore projectStateStore,
    IGitProcessRunner gitProcessRunner,
    Func<UserSettingsStore> userSettingsStoreFactory,
    IDoctorCredentialStoreProbe credentialStoreProbe,
    IDoctorTemporaryFileProbe temporaryFileProbe,
    IDoctorRemoteSourceProbe remoteSourceProbe,
    TimeSpan? networkSourceTimeout = null,
    TimeSpan? overallNetworkTimeout = null
) : IDoctorService
{
    public const int NetworkSourceTimeoutSeconds = 5;
    private const int GitVersionTimeoutSeconds = 3;
    private const int OverallNetworkTimeoutSeconds = 20;
    private readonly TimeSpan _networkSourceTimeout =
        networkSourceTimeout ?? TimeSpan.FromSeconds(NetworkSourceTimeoutSeconds);
    private readonly TimeSpan _overallNetworkTimeout =
        overallNetworkTimeout ?? TimeSpan.FromSeconds(OverallNetworkTimeoutSeconds);

    public async Task<DoctorReport> InspectAsync(
        string projectDirectory,
        bool checkNetwork,
        CancellationToken cancellationToken = default
    )
    {
        var checks = new List<DoctorCheckResult>();
        var project = await InspectProjectAsync(projectDirectory, checks);

        await AddGitCheckAsync(project.Configuration, checks, cancellationToken);
        AddCredentialStoreCheck(checks);

        var userState = await InspectUserStateAsync(
            projectDirectory,
            project.HasAnyState,
            project.Configuration,
            checks
        );
        AddCacheChecks(projectDirectory, project.Configuration, checks);

        if (checkNetwork && project.State is { } state)
        {
            await AddNetworkChecksAsync(
                projectDirectory,
                state,
                userState.SettingsStore,
                userState.IgnoreContext,
                checks,
                cancellationToken
            );
        }
        else if (project.Configuration is not null)
        {
            var hasRemoteSources = project.Configuration.Sources.Any(source =>
                source is ProjectConfiguration.GitSource or ProjectConfiguration.OciSource
            );
            if (hasRemoteSources)
            {
                checks.Add(
                    new DoctorCheckResult(
                        "Network",
                        DoctorCheckStatus.Info,
                        "Remote source checks were not run.",
                        "Run 'luna doctor --network' to opt in to bounded configured-source checks."
                    )
                );
            }
        }

        return new DoctorReport(checks);
    }

    private async Task<ProjectInspection> InspectProjectAsync(
        string projectDirectory,
        List<DoctorCheckResult> checks
    )
    {
        var configurationPath = fileSystem.Path.Combine(
            projectDirectory,
            ProjectStateStore.ConfigurationFileName
        );
        var lockPath = fileSystem.Path.Combine(projectDirectory, ProjectStateStore.LockFileName);
        var hasConfiguration =
            fileSystem.File.Exists(configurationPath)
            || fileSystem.Directory.Exists(configurationPath);
        var hasLock = fileSystem.File.Exists(lockPath) || fileSystem.Directory.Exists(lockPath);
        var hasAnyState = hasConfiguration || hasLock;
        if (!hasAnyState)
        {
            checks.Add(
                new DoctorCheckResult(
                    "Project state",
                    DoctorCheckStatus.Info,
                    "No LunaPack project state was found; no project operations are configured.",
                    "Run 'luna init' before adding project sources or packs."
                )
            );
            return new ProjectInspection(false, null);
        }

        var pathError =
            ValidateStatePath(configurationPath, ProjectStateStore.ConfigurationFileName)
            ?? ValidateStatePath(lockPath, ProjectStateStore.LockFileName);
        if (pathError is not null)
        {
            checks.Add(
                new DoctorCheckResult(
                    "Project state",
                    DoctorCheckStatus.Fail,
                    pathError,
                    "Replace linked or non-file state paths with regular project state files."
                )
            );
            return new ProjectInspection(true, null);
        }

        var state = await projectStateStore.LoadAsync(projectDirectory);
        if (state.Value is not { } loaded)
        {
            checks.Add(
                new DoctorCheckResult(
                    "Project state",
                    DoctorCheckStatus.Fail,
                    "Project state is missing, unreadable, or invalid.",
                    "Restore or repair 'lunapack.yml' and 'lunapack-lock.yml', then rerun 'luna doctor'."
                )
            );
            return new ProjectInspection(true, null);
        }

        checks.Add(
            new DoctorCheckResult(
                "Project state",
                DoctorCheckStatus.Pass,
                "Project configuration and lock state are readable and valid."
            )
        );
        return new ProjectInspection(true, loaded);
    }

    private async Task AddGitCheckAsync(
        ProjectConfiguration? configuration,
        List<DoctorCheckResult> checks,
        CancellationToken cancellationToken
    )
    {
        var requiresGit =
            configuration?.Sources.Any(source => source is ProjectConfiguration.GitSource) is true;
        var git = await gitProcessRunner.RunAsync(
            ["--version"],
            TimeSpan.FromSeconds(GitVersionTimeoutSeconds),
            cancellationToken
        );
        if (git.IsSuccess)
        {
            checks.Add(new DoctorCheckResult("Git", DoctorCheckStatus.Pass, "Git is available."));
            return;
        }

        checks.Add(
            new DoctorCheckResult(
                "Git",
                requiresGit ? DoctorCheckStatus.Fail : DoctorCheckStatus.Info,
                requiresGit
                    ? "Git is unavailable, but the project configures Git sources."
                    : "Git is unavailable; no configured Git operation currently requires it.",
                "Install Git and ensure a trusted Git executable is available on PATH."
            )
        );
    }

    private void AddCredentialStoreCheck(List<DoctorCheckResult> checks)
    {
        var result = credentialStoreProbe.Probe();
        checks.Add(
            new DoctorCheckResult(
                "Credential storage",
                result.IsAvailable ? DoctorCheckStatus.Pass : DoctorCheckStatus.Info,
                result.Message,
                result.IsAvailable
                    ? null
                    : "Install or repair the platform credential-store runtime before storing OCI credentials."
            )
        );
    }

    private async Task<UserInspection> InspectUserStateAsync(
        string projectDirectory,
        bool hasProjectState,
        ProjectConfiguration? configuration,
        List<DoctorCheckResult> checks
    )
    {
        UserSettingsStore settingsStore;
        try
        {
            settingsStore = userSettingsStoreFactory();
        }
        catch (Exception exception)
            when (exception
                    is InvalidOperationException
                        or IOException
                        or UnauthorizedAccessException
                        or ArgumentException
                        or NotSupportedException
            )
        {
            checks.Add(
                new DoctorCheckResult(
                    "User settings",
                    hasProjectState ? DoctorCheckStatus.Fail : DoctorCheckStatus.Info,
                    "The LunaPack user profile could not be resolved.",
                    "Set a valid absolute LUNAPACK_USER_PROFILE or repair the current user profile."
                )
            );
            return new UserInspection(null, null);
        }

        var settings = await settingsStore.LoadAsync();
        checks.Add(
            settings.IsSuccess
                ? new DoctorCheckResult(
                    "User settings",
                    DoctorCheckStatus.Pass,
                    "User settings are readable and valid."
                )
                : new DoctorCheckResult(
                    "User settings",
                    hasProjectState ? DoctorCheckStatus.Fail : DoctorCheckStatus.Info,
                    "User settings are unreadable or invalid.",
                    "Repair the LunaPack config.yml file in the user profile."
                )
        );

        var ignoreService = new ConfiguredSourceIgnoreService(fileSystem, settingsStore);
        var ignore = await ignoreService.LoadAsync(projectDirectory);
        var hasSources = configuration?.Sources.Count > 0;
        checks.Add(
            ignore.IsSuccess
                ? new DoctorCheckResult(
                    "Source-ignore state",
                    DoctorCheckStatus.Pass,
                    "Local source-ignore state is readable and valid."
                )
                : new DoctorCheckResult(
                    "Source-ignore state",
                    hasSources ? DoctorCheckStatus.Fail : DoctorCheckStatus.Info,
                    "Local source-ignore state is unreadable or invalid.",
                    "Repair the LunaPack source-ignores.yml file in the user profile."
                )
        );

        AddDirectoryCheck(
            "Profile directory",
            settingsStore.SettingsDirectory,
            hasProjectState,
            checks
        );
        return new UserInspection(
            settings.IsSuccess ? settingsStore : null,
            settings.IsSuccess && ignore.IsSuccess ? ignore.Value : null
        );
    }

    private void AddCacheChecks(
        string projectDirectory,
        ProjectConfiguration? configuration,
        List<DoctorCheckResult> checks
    )
    {
        var requiresProjectCache =
            configuration?.Sources.Any(source => source is ProjectConfiguration.GitSource) is true;
        var requiresSourceCache =
            configuration is not null
            && configuration.Links.Values.Any(link =>
                configuration.Sources.Any(source =>
                    string.Equals(source.Name, link.Source, StringComparison.Ordinal)
                    && source is ProjectConfiguration.GitSource
                )
            );
        string sourceCacheRoot;
        try
        {
            sourceCacheRoot = LinkSourceCacheRoot.Resolve(fileSystem);
        }
        catch (Exception exception)
            when (exception
                    is ArgumentException
                        or IOException
                        or UnauthorizedAccessException
                        or NotSupportedException
            )
        {
            checks.Add(
                new DoctorCheckResult(
                    "Source cache",
                    requiresSourceCache ? DoctorCheckStatus.Fail : DoctorCheckStatus.Info,
                    "The operating-system source cache path could not be resolved.",
                    "Set a valid absolute cache location for the current operating system."
                )
            );
            return;
        }

        if (!fileSystem.Path.IsPathRooted(sourceCacheRoot))
        {
            checks.Add(
                new DoctorCheckResult(
                    "Source cache",
                    requiresSourceCache ? DoctorCheckStatus.Fail : DoctorCheckStatus.Info,
                    "The source cache path is not absolute.",
                    "Set an absolute cache location for the current operating system."
                )
            );
        }
        else
        {
            AddDirectoryCheck("Source cache", sourceCacheRoot, requiresSourceCache, checks);
        }

        AddDirectoryCheck(
            "Project Git cache",
            fileSystem.Path.Combine(projectDirectory, ".lunapack", "git-sources"),
            requiresProjectCache,
            checks
        );
    }

    private void AddDirectoryCheck(
        string name,
        string directory,
        bool required,
        List<DoctorCheckResult> checks
    )
    {
        if (fileSystem.File.Exists(directory))
        {
            checks.Add(
                new DoctorCheckResult(
                    name,
                    required ? DoctorCheckStatus.Fail : DoctorCheckStatus.Info,
                    $"'{directory}' is a file where a directory is expected.",
                    $"Repair '{directory}' and rerun 'luna doctor'."
                )
            );
            return;
        }

        if (!fileSystem.Directory.Exists(directory))
        {
            checks.Add(
                new DoctorCheckResult(
                    name,
                    DoctorCheckStatus.Info,
                    $"'{directory}' does not exist; doctor did not create it.",
                    required
                        ? "The normal command will create this cache or profile directory when its parent is writable."
                        : null
                )
            );
            return;
        }

        var probe = temporaryFileProbe.Probe(directory);
        checks.Add(
            new DoctorCheckResult(
                name,
                probe.IsSuccess ? DoctorCheckStatus.Pass
                    : required ? DoctorCheckStatus.Fail
                    : DoctorCheckStatus.Info,
                probe.Message,
                probe.IsSuccess
                    ? null
                    : $"Grant the current user safe write access to '{directory}', then rerun 'luna doctor'."
            )
        );
    }

    private async Task AddNetworkChecksAsync(
        string projectDirectory,
        ProjectState projectState,
        UserSettingsStore? settingsStore,
        ConfiguredSourceIgnoreService.SourceIgnoreContext? ignoreContext,
        List<DoctorCheckResult> checks,
        CancellationToken cancellationToken
    )
    {
        var configuration = projectState.Configuration;
        var remoteSources = configuration
            .Sources.Where(source =>
                source is ProjectConfiguration.GitSource or ProjectConfiguration.OciSource
            )
            .ToList();
        if (remoteSources.Count == 0)
        {
            checks.Add(
                new DoctorCheckResult(
                    "Network",
                    DoctorCheckStatus.Info,
                    "No configured remote source requires a network check."
                )
            );
            return;
        }

        if (settingsStore is null || ignoreContext is null)
        {
            checks.Add(
                new DoctorCheckResult(
                    "Network",
                    DoctorCheckStatus.Info,
                    "Network checks were skipped because local source-ignore state is not valid.",
                    "Repair source-ignore state, then rerun 'luna doctor --network'."
                )
            );
            return;
        }

        var ignoreService = new ConfiguredSourceIgnoreService(fileSystem, settingsStore);
        using var overallTimeout = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken
        );
        overallTimeout.CancelAfter(_overallNetworkTimeout);
        foreach (var source in remoteSources)
        {
            await AddNetworkCheckAsync(
                projectDirectory,
                projectState,
                source,
                ignoreService,
                ignoreContext,
                checks,
                overallTimeout.Token,
                cancellationToken
            );
        }
    }

    private async Task AddNetworkCheckAsync(
        string projectDirectory,
        ProjectState projectState,
        ProjectConfiguration.Source source,
        ConfiguredSourceIgnoreService ignoreService,
        ConfiguredSourceIgnoreService.SourceIgnoreContext ignoreContext,
        List<DoctorCheckResult> checks,
        CancellationToken overallCancellationToken,
        CancellationToken cancellationToken
    )
    {
        var ignored = ignoreService.IsIgnored(ignoreContext, source);
        if (!ignored.IsSuccess)
        {
            checks.Add(
                new DoctorCheckResult(
                    $"Source {source.Name}",
                    DoctorCheckStatus.Fail,
                    "The configured source identity could not be checked safely."
                )
            );
            return;
        }

        if (ignored.Value)
        {
            checks.Add(
                new DoctorCheckResult(
                    $"Source {source.Name}",
                    DoctorCheckStatus.Info,
                    "Locally ignored; no network request was made."
                )
            );
            return;
        }

        if (overallCancellationToken.IsCancellationRequested)
        {
            checks.Add(
                new DoctorCheckResult(
                    $"Source {source.Name}",
                    DoctorCheckStatus.Info,
                    "Not checked because the overall network budget expired."
                )
            );
            return;
        }

        using var sourceTimeout = CancellationTokenSource.CreateLinkedTokenSource(
            overallCancellationToken
        );
        sourceTimeout.CancelAfter(_networkSourceTimeout);
        try
        {
            var probe = await ProbeSourceAsync(
                projectDirectory,
                projectState,
                source,
                sourceTimeout.Token
            );
            AddRemoteProbeResult(source, probe, checks);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            checks.Add(
                new DoctorCheckResult(
                    $"Source {source.Name}",
                    DoctorCheckStatus.Fail,
                    "The configured-source probe timed out.",
                    "Verify network, DNS, and source host availability, then rerun 'luna doctor --network'."
                )
            );
        }
    }

    private async Task<DoctorRemoteProbeResult> ProbeSourceAsync(
        string projectDirectory,
        ProjectState projectState,
        ProjectConfiguration.Source source,
        CancellationToken cancellationToken
    ) =>
        source switch
        {
            ProjectConfiguration.GitSource git => await remoteSourceProbe
                .ProbeGitAsync(git, cancellationToken)
                .WaitAsync(cancellationToken),
            ProjectConfiguration.OciSource oci => await remoteSourceProbe
                .ProbeOciAsync(
                    projectDirectory,
                    oci,
                    FindOciProbePackId(projectState, oci),
                    cancellationToken
                )
                .WaitAsync(cancellationToken),
            _ => throw new InvalidOperationException("Unexpected remote source type."),
        };

    private static string? FindOciProbePackId(
        ProjectState projectState,
        ProjectConfiguration.OciSource source
    )
    {
        return projectState
            .LockFile.Packs.Where(pack =>
                string.Equals(pack.SourceName, source.Name, StringComparison.Ordinal)
                && pack.OciSource is not null
            )
            .Select(pack => pack.Id)
            .Order(StringComparer.Ordinal)
            .FirstOrDefault();
    }

    private static void AddRemoteProbeResult(
        ProjectConfiguration.Source source,
        DoctorRemoteProbeResult probe,
        List<DoctorCheckResult> checks
    )
    {
        var status = probe.Outcome switch
        {
            DoctorRemoteProbeOutcome.Success => DoctorCheckStatus.Pass,
            DoctorRemoteProbeOutcome.Failure => DoctorCheckStatus.Fail,
            _ => DoctorCheckStatus.Info,
        };
        checks.Add(
            new DoctorCheckResult($"Source {source.Name}", status, probe.Message, probe.Remediation)
        );
    }

    private string? ValidateStatePath(string path, string fileName)
    {
        try
        {
            if (fileSystem.Directory.Exists(path))
            {
                return $"'{fileName}' must be a regular file.";
            }

            if (
                fileSystem.File.Exists(path)
                && fileSystem.File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint)
            )
            {
                return $"'{fileName}' cannot be a link or reparse point during doctor checks.";
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return $"'{fileName}' could not be inspected safely.";
        }

        return null;
    }

    private sealed record ProjectInspection(bool HasAnyState, ProjectState? State)
    {
        public ProjectConfiguration? Configuration => State?.Configuration;
    }

    private sealed record UserInspection(
        UserSettingsStore? SettingsStore,
        ConfiguredSourceIgnoreService.SourceIgnoreContext? IgnoreContext
    );
}
