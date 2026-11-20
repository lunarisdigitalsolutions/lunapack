using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using Lunapack.Cli.Application.CommandExecution;

namespace Lunapack.Cli.Sources.Git;

internal sealed class GitProcessRunner : IGitProcessRunner
{
    private const int MaximumDiagnosticCharacters = 64 * 1024;
    private static readonly Encoding _gitTextEncoding = new UTF8Encoding(false);
    private readonly GitCheckoutFilterGuard _checkoutFilterGuard;
    private readonly string _executable;
    private readonly bool _isGitExecutable;
    private readonly bool _nonInteractive;
    private readonly GitTransportSecurity _transportSecurity;

    public GitProcessRunner(string executable = "git", bool nonInteractive = false)
    {
        _executable = executable;
        _nonInteractive = nonInteractive;
        _isGitExecutable = string.Equals(
            Path.GetFileNameWithoutExtension(executable),
            "git",
            StringComparison.OrdinalIgnoreCase
        );
        _transportSecurity = new GitTransportSecurity(ExecuteSecurityProbeAsync);
        _checkoutFilterGuard = new GitCheckoutFilterGuard(ExecuteSecurityProbeAsync);
    }

    public Task<ManifestOperationResult<GitProcessOutput>> RunAsync(
        IReadOnlyList<string> arguments,
        TimeSpan timeout,
        CancellationToken cancellationToken
    ) => RunAsync(arguments, timeout, GitTransportMode.SecureOnly, cancellationToken);

    public Task<ManifestOperationResult<GitProcessOutput>> RunAsync(
        IReadOnlyList<string> arguments,
        TimeSpan timeout,
        GitTransportMode transportMode,
        CancellationToken cancellationToken
    ) => RunAsync(arguments, timeout, transportMode, null, cancellationToken);

    public async Task<ManifestOperationResult<GitProcessOutput>> RunAsync(
        IReadOnlyList<string> arguments,
        TimeSpan timeout,
        GitTransportMode transportMode,
        string? repositoryUrl,
        CancellationToken cancellationToken
    )
    {
        if (timeout <= TimeSpan.Zero)
        {
            return ManifestOperationResult<GitProcessOutput>.Failure(
                "Git operation timeout must be greater than zero."
            );
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return ManifestOperationResult<GitProcessOutput>.Failure("Git operation was canceled.");
        }
        if (
            repositoryUrl is not null
            && !string.Equals(repositoryUrl, repositoryUrl.Trim(), StringComparison.Ordinal)
        )
        {
            return ManifestOperationResult<GitProcessOutput>.Failure(
                "Git repository URL must not contain leading or trailing whitespace."
            );
        }

        var resolvedExecutable = ResolveExecutable();
        if (resolvedExecutable is null)
        {
            return ManifestOperationResult<GitProcessOutput>.Failure(
                $"Unable to locate a trusted Git executable for '{_executable}'."
            );
        }

        using var timeoutCancellation = new CancellationTokenSource(timeout);
        using var operationCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            timeoutCancellation.Token
        );
        try
        {
            return await ExecuteConfiguredAsync(
                arguments,
                resolvedExecutable,
                timeout,
                transportMode,
                repositoryUrl,
                operationCancellation.Token,
                cancellationToken
            );
        }
        catch (Win32Exception exception)
        {
            return ManifestOperationResult<GitProcessOutput>.Failure(
                $"Unable to run Git executable '{_executable}': {exception.Message}"
            );
        }
    }

    private async Task<ManifestOperationResult<GitProcessOutput>> ExecuteConfiguredAsync(
        IReadOnlyList<string> arguments,
        string resolvedExecutable,
        TimeSpan timeout,
        GitTransportMode transportMode,
        string? repositoryUrl,
        CancellationToken operationCancellation,
        CancellationToken callerCancellation
    )
    {
        var workingDirectory = GitTransportSecurity.GetGitWorkingDirectory(arguments);
        var effectiveRepository = await ResolveEffectiveRepositoryIfNeededAsync(
            repositoryUrl,
            timeout,
            transportMode,
            workingDirectory,
            operationCancellation,
            callerCancellation
        );
        if (!effectiveRepository.IsSuccess)
        {
            return ManifestOperationResult<GitProcessOutput>.Failure(
                effectiveRepository.Error ?? "Unable to resolve the effective Git repository URL."
            );
        }
        var effectiveRepositoryUrl = effectiveRepository.Value;

        var startInfo = CreateStartInfo(resolvedExecutable, arguments, transportMode);
        if (_isGitExecutable)
        {
            GitTransportSecurity.ConfigureTransportEnvironment(
                startInfo.Environment,
                transportMode,
                repositoryUrl
            );
            var configurationError = GitTransportSecurity.ConfigureSecureHttps(
                startInfo.Environment,
                transportMode,
                effectiveRepositoryUrl
            );
            if (configurationError is not null)
            {
                return ManifestOperationResult<GitProcessOutput>.Failure(configurationError);
            }

            var verification = await _transportSecurity.VerifySecureHttpsAsync(
                timeout,
                transportMode,
                effectiveRepositoryUrl,
                workingDirectory,
                operationCancellation,
                callerCancellation
            );
            if (!verification.IsSuccess)
            {
                return ManifestOperationResult<GitProcessOutput>.Failure(
                    verification.Error ?? "Unable to verify secure Git HTTPS settings."
                );
            }

            var checkoutVerification = await _checkoutFilterGuard.VerifyAsync(
                arguments,
                timeout,
                transportMode,
                effectiveRepositoryUrl,
                operationCancellation,
                callerCancellation
            );
            if (!checkoutVerification.IsSuccess)
            {
                return ManifestOperationResult<GitProcessOutput>.Failure(
                    checkoutVerification.Error ?? "Unable to verify Git checkout filters."
                );
            }
        }

        return await ExecuteProcessAsync(
            startInfo,
            timeout,
            operationCancellation,
            callerCancellation
        );
    }

    private async Task<ManifestOperationResult<string?>> ResolveEffectiveRepositoryIfNeededAsync(
        string? repositoryUrl,
        TimeSpan timeout,
        GitTransportMode transportMode,
        string? workingDirectory,
        CancellationToken operationCancellation,
        CancellationToken callerCancellation
    )
    {
        if (
            !_isGitExecutable
            || transportMode != GitTransportMode.SecureOnly
            || repositoryUrl is null
        )
        {
            return ManifestOperationResult<string?>.Success(repositoryUrl);
        }

        var resolved = await _transportSecurity.ResolveEffectiveRepositoryUrlAsync(
            repositoryUrl,
            timeout,
            transportMode,
            workingDirectory,
            operationCancellation,
            callerCancellation
        );
        return resolved.Value is { } value
            ? ManifestOperationResult<string?>.Success(value)
            : ManifestOperationResult<string?>.Failure(
                resolved.Error ?? "Unable to resolve the effective Git repository URL."
            );
    }

    private static async Task<ManifestOperationResult<GitProcessOutput>> ExecuteProcessAsync(
        ProcessStartInfo startInfo,
        TimeSpan timeout,
        CancellationToken operationCancellation,
        CancellationToken callerCancellation,
        string? standardInput = null,
        int maximumOutputCharacters = MaximumDiagnosticCharacters
    )
    {
        startInfo.RedirectStandardInput = standardInput is not null;
        if (standardInput is not null)
        {
            startInfo.StandardInputEncoding = _gitTextEncoding;
        }
        using var process = new Process { StartInfo = startInfo };
        if (!process.Start())
        {
            return ManifestOperationResult<GitProcessOutput>.Failure(
                "Unable to start the Git executable."
            );
        }

        var standardOutput = ReadBoundedAsync(
            process.StandardOutput,
            maximumOutputCharacters,
            operationCancellation
        );
        var standardError = ReadBoundedAsync(
            process.StandardError,
            MaximumDiagnosticCharacters,
            operationCancellation
        );
        try
        {
            if (standardInput is not null)
            {
                await process.StandardInput.WriteAsync(
                    standardInput.AsMemory(),
                    operationCancellation
                );
                process.StandardInput.Close();
            }
            await process.WaitForExitAsync(operationCancellation);
            var (capturedOutput, outputTruncated) = await standardOutput;
            var (capturedError, errorTruncated) = await standardError;
            var output = new GitProcessOutput(
                capturedOutput,
                capturedError,
                outputTruncated,
                errorTruncated
            );
            return process.ExitCode == 0
                ? ManifestOperationResult<GitProcessOutput>.Success(output)
                : ManifestOperationResult<GitProcessOutput>.Failure(
                    CreateExitCodeError(process.ExitCode, output)
                );
        }
        catch (IOException)
        {
            await StopAsync(process);
            await DrainOutputAsync(standardOutput, standardError);
            return ManifestOperationResult<GitProcessOutput>.Failure(
                "Git operation could not write command input."
            );
        }
        catch (OperationCanceledException)
        {
            await StopAsync(process);
            await DrainOutputAsync(standardOutput, standardError);

            return ManifestOperationResult<GitProcessOutput>.Failure(
                callerCancellation.IsCancellationRequested
                    ? "Git operation was canceled."
                    : $"Git operation timed out after {timeout.TotalSeconds:0} seconds."
            );
        }
    }

    private ProcessStartInfo CreateStartInfo(
        string resolvedExecutable,
        IReadOnlyList<string> arguments,
        GitTransportMode transportMode
    )
    {
        var startInfo = new ProcessStartInfo(resolvedExecutable)
        {
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            StandardErrorEncoding = _gitTextEncoding,
            StandardOutputEncoding = _gitTextEncoding,
            UseShellExecute = false,
        };
        startInfo.Environment["GIT_TERMINAL_PROMPT"] = "0";
        if (_nonInteractive)
        {
            startInfo.WorkingDirectory = Path.GetTempPath();
            startInfo.Environment["GCM_INTERACTIVE"] = "Never";
            startInfo.Environment["GIT_OPTIONAL_LOCKS"] = "0";
            startInfo.Environment["GIT_SSH_COMMAND"] = CreateSshCommand();
            startInfo.Environment["PATH"] = CreateSanitizedPath();
            startInfo.Environment["SSH_ASKPASS_REQUIRE"] = "never";
            startInfo.Environment.Remove("GIT_ASKPASS");
            startInfo.Environment.Remove("SSH_ASKPASS");
            startInfo.Environment["GIT_CONFIG_NOSYSTEM"] = "1";
            startInfo.Environment["GIT_CONFIG_GLOBAL"] = OperatingSystem.IsWindows()
                ? "NUL"
                : "/dev/null";
            startInfo.Environment.Remove("GIT_CONFIG_PARAMETERS");
            foreach (
                var variable in startInfo
                    .Environment.Keys.Where(name =>
                        name.StartsWith("GIT_CONFIG_KEY_", StringComparison.OrdinalIgnoreCase)
                        || name.StartsWith("GIT_CONFIG_VALUE_", StringComparison.OrdinalIgnoreCase)
                    )
                    .ToArray()
            )
            {
                startInfo.Environment.Remove(variable);
            }
            startInfo.Environment.Remove("GIT_CONFIG_COUNT");
            startInfo.Environment.Remove("GIT_DIR");
            startInfo.Environment.Remove("GIT_WORK_TREE");
            startInfo.Environment.Remove("GIT_COMMON_DIR");
            startInfo.Environment.Remove("GIT_EXEC_PATH");
            startInfo.Environment.Remove("GIT_SSL_CERT");
            startInfo.Environment.Remove("GIT_SSL_KEY");
            startInfo.Environment.Remove("GIT_SSL_CAINFO");
            startInfo.Environment.Remove("GIT_SSL_CAPATH");
            foreach (
                var variable in startInfo
                    .Environment.Keys.Where(name =>
                        name.StartsWith("GIT_TRACE", StringComparison.OrdinalIgnoreCase)
                    )
                    .ToArray()
            )
            {
                startInfo.Environment.Remove(variable);
            }
        }

        if (_isGitExecutable)
        {
            foreach (var argument in GitTransportSecurity.CreateTransportArguments(transportMode))
            {
                startInfo.ArgumentList.Add(argument);
            }
        }

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        return startInfo;
    }

    internal ProcessStartInfo CreateStartInfoForTests(string resolvedExecutable) =>
        CreateStartInfo(resolvedExecutable, [], GitTransportMode.SecureOnly);

    private static string CreateSshCommand()
    {
        var ssh = ResolveExecutable(
            "ssh",
            Environment.GetEnvironmentVariable("PATH"),
            Path.GetFullPath("."),
            OperatingSystem.IsWindows(),
            [Path.GetFullPath("."), Path.GetFullPath(Path.GetTempPath())]
        );
        if (ssh is null || ssh.IndexOfAny(['"', '$', '`', '\'', '\r', '\n']) >= 0)
        {
            return $"\"{Path.Combine(Path.GetTempPath(), $".lunapack-ssh-unavailable-{Guid.NewGuid():N}")}\"";
        }

        return $"\"{ssh}\" -oBatchMode=yes -oStrictHostKeyChecking=yes";
    }

    private static string CreateSanitizedPath()
    {
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        var exclusions = new[] { Path.GetFullPath("."), Path.GetFullPath(Path.GetTempPath()) };
        var entries = new List<string>();
        foreach (
            var entry in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(
                Path.PathSeparator,
                StringSplitOptions.RemoveEmptyEntries
            )
        )
        {
            var trimmedEntry = entry.Trim().Trim('"');
            if (
                !Path.IsPathRooted(trimmedEntry)
                || trimmedEntry.StartsWith(@"\\", StringComparison.Ordinal)
            )
            {
                continue;
            }

            var absoluteEntry = Path.TrimEndingDirectorySeparator(Path.GetFullPath(trimmedEntry));
            if (
                exclusions.Any(excluded =>
                    IsSameOrDescendant(absoluteEntry, Path.GetFullPath(excluded), comparison)
                )
            )
            {
                continue;
            }

            if (!entries.Any(existing => string.Equals(existing, absoluteEntry, comparison)))
            {
                entries.Add(absoluteEntry);
            }
        }

        return string.Join(Path.PathSeparator, entries);
    }

    internal static string CreateExitCodeError(int exitCode, GitProcessOutput output)
    {
        var diagnostic = $"{output.StandardError}\n{output.StandardOutput}";
        var guidance =
            ContainsAny(
                diagnostic,
                "authentication failed",
                "could not read username",
                "could not read password",
                "terminal prompts disabled",
                "no credentials available",
                "permission denied (publickey)",
                "no supported authentication methods available",
                "too many authentication failures"
            )
                ? "Git authentication failed. Configure the operating-system Git credential helper or SSH credentials for this source."
            : ContainsAny(diagnostic, "transport 'http' not allowed", "transport 'git' not allowed")
                ? "Git source was rewritten to an insecure transport and blocked. Remove the matching url.*.insteadOf rule or use a secure source URL."
            : ContainsAny(
                diagnostic,
                "permission denied",
                "access denied",
                "not authorized",
                "repository not found",
                "does not appear to be a git repository"
            )
                ? "Git access was denied or the repository was not found. Verify the repository URL, permissions, and configured Git credentials or SSH identity."
            : ContainsAny(
                diagnostic,
                "certificate verify failed",
                "ssl certificate problem",
                "tls certificate",
                "schannel"
            )
                ? "Git TLS certificate verification failed. Fix the certificate trust configuration; do not disable certificate checks."
            : ContainsAny(
                diagnostic,
                "could not resolve host",
                "temporary failure in name resolution",
                "failed to connect",
                "connection refused",
                "network is unreachable",
                "no route to host",
                "connection timed out",
                "operation timed out"
            )
                ? "Git host is unavailable. Check network and DNS access, then retry."
            : "Check the Git source URL, repository access, and host availability.";

        return $"Git exited with code {exitCode}: {guidance}";
    }

    private Task<ManifestOperationResult<GitProcessOutput>> ExecuteSecurityProbeAsync(
        GitSecurityProbeRequest request,
        CancellationToken operationCancellation,
        CancellationToken callerCancellation
    )
    {
        var resolvedExecutable = ResolveExecutable();
        if (resolvedExecutable is null)
        {
            return Task.FromResult(
                ManifestOperationResult<GitProcessOutput>.Failure(
                    $"Unable to locate a trusted Git executable for '{_executable}'."
                )
            );
        }

        var startInfo = CreateStartInfo(
            resolvedExecutable,
            request.Arguments,
            request.TransportMode
        );
        GitTransportSecurity.ConfigureTransportEnvironment(
            startInfo.Environment,
            request.TransportMode,
            request.RepositoryUrl
        );
        if (request.GitIndexPath is not null)
        {
            startInfo.Environment["GIT_INDEX_FILE"] = request.GitIndexPath;
        }
        var configurationError = GitTransportSecurity.ConfigureSecureHttps(
            startInfo.Environment,
            request.TransportMode,
            request.RepositoryUrl
        );
        return configurationError is null
            ? ExecuteProcessAsync(
                startInfo,
                request.Timeout,
                operationCancellation,
                callerCancellation,
                request.StandardInput,
                request.MaximumOutputCharacters ?? MaximumDiagnosticCharacters
            )
            : Task.FromResult(
                ManifestOperationResult<GitProcessOutput>.Failure(configurationError)
            );
    }

    private static bool ContainsAny(string value, params string[] fragments) =>
        fragments.Any(fragment => value.Contains(fragment, StringComparison.OrdinalIgnoreCase));

    private static async Task<(string Value, bool Truncated)> ReadBoundedAsync(
        StreamReader reader,
        int maximumCharacters,
        CancellationToken cancellationToken
    )
    {
        var buffer = new char[4096];
        var captured = new StringBuilder(maximumCharacters);
        var truncated = false;
        while (true)
        {
            var charactersRead = await reader.ReadAsync(buffer.AsMemory(), cancellationToken);
            if (charactersRead == 0)
            {
                return (captured.ToString(), truncated);
            }

            var remainingCapacity = maximumCharacters - captured.Length;
            if (remainingCapacity > 0)
            {
                captured.Append(buffer, 0, Math.Min(charactersRead, remainingCapacity));
            }
            if (charactersRead > remainingCapacity)
            {
                truncated = true;
            }
        }
    }

    private static async Task DrainOutputAsync(
        Task<(string Value, bool Truncated)> standardOutput,
        Task<(string Value, bool Truncated)> standardError
    )
    {
        try
        {
            await Task.WhenAll(standardOutput, standardError);
        }
        catch (OperationCanceledException) { }
        catch (IOException) { }
    }

    private string? ResolveExecutable()
    {
        return ResolveExecutable(
            _executable,
            Environment.GetEnvironmentVariable("PATH"),
            Path.GetFullPath("."),
            OperatingSystem.IsWindows()
        );
    }

    internal static string? ResolveExecutable(
        string executable,
        string? searchPath,
        string currentDirectory,
        bool isWindows,
        IReadOnlyList<string>? excludedDirectories = null
    )
    {
        if (Path.IsPathRooted(executable))
        {
            return File.Exists(executable) ? Path.GetFullPath(executable) : null;
        }

        if (
            executable.Contains(Path.DirectorySeparatorChar, StringComparison.Ordinal)
            || executable.Contains(Path.AltDirectorySeparatorChar, StringComparison.Ordinal)
        )
        {
            var absolute = Path.GetFullPath(executable);
            return File.Exists(absolute) ? absolute : null;
        }

        if (string.IsNullOrEmpty(searchPath))
        {
            return null;
        }

        var normalizedCurrentDirectory = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(currentDirectory)
        );
        var comparison = isWindows ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var candidates = isWindows ? CreateExecutableCandidates(executable) : [executable];
        foreach (
            var entry in searchPath.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
        )
        {
            var trimmedEntry = entry.Trim().Trim('"');
            if (
                !Path.IsPathRooted(trimmedEntry)
                || trimmedEntry.StartsWith(@"\\", StringComparison.Ordinal)
            )
            {
                continue;
            }

            var absoluteEntry = Path.TrimEndingDirectorySeparator(Path.GetFullPath(trimmedEntry));
            var exclusions = excludedDirectories ?? (IReadOnlyList<string>)[];
            if (
                IsSameOrDescendant(absoluteEntry, normalizedCurrentDirectory, comparison)
                || exclusions.Any(excluded =>
                    IsSameOrDescendant(absoluteEntry, Path.GetFullPath(excluded), comparison)
                )
            )
            {
                continue;
            }

            foreach (var candidate in candidates)
            {
                var executablePath = Path.Combine(absoluteEntry, candidate);
                if (File.Exists(executablePath))
                {
                    return executablePath;
                }
            }
        }

        return null;
    }

    private static bool IsSameOrDescendant(string path, string parent, StringComparison comparison)
    {
        var normalizedParent = Path.TrimEndingDirectorySeparator(parent);
        var prefix = normalizedParent.EndsWith(Path.DirectorySeparatorChar)
            ? normalizedParent
            : normalizedParent + Path.DirectorySeparatorChar;
        return string.Equals(path, normalizedParent, comparison)
            || path.StartsWith(prefix, comparison);
    }

    private static IReadOnlyList<string> CreateExecutableCandidates(string executable)
    {
        if (Path.HasExtension(executable))
        {
            return [executable];
        }

        return [$"{executable}.exe", $"{executable}.com"];
    }

    private static async Task StopAsync(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
            return;
        }

        try
        {
            await process.WaitForExitAsync();
        }
        catch (InvalidOperationException)
        {
            // The process exited while cleanup raced with cancellation.
        }
    }
}
