using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using Lunapack.Cli.Application.CommandExecution;

namespace Lunapack.Cli.Sources.Git;

internal sealed class GitProcessRunner(string executable = "git", bool nonInteractive = false)
    : IGitProcessRunner
{
    private const int MaximumDiagnosticCharacters = 64 * 1024;

    public async Task<ManifestOperationResult<GitProcessOutput>> RunAsync(
        IReadOnlyList<string> arguments,
        TimeSpan timeout,
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

        var resolvedExecutable = ResolveExecutable();
        if (resolvedExecutable is null)
        {
            return ManifestOperationResult<GitProcessOutput>.Failure(
                $"Unable to locate a trusted Git executable for '{executable}'."
            );
        }

        var startInfo = CreateStartInfo(resolvedExecutable);
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        try
        {
            return await RunProcessAsync(startInfo, timeout, cancellationToken);
        }
        catch (Win32Exception exception)
        {
            return ManifestOperationResult<GitProcessOutput>.Failure(
                $"Unable to run the resolved Git executable: {exception.Message}"
            );
        }
    }

    private ProcessStartInfo CreateStartInfo(string resolvedExecutable)
    {
        var startInfo = new ProcessStartInfo(resolvedExecutable)
        {
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };
        startInfo.Environment["GIT_TERMINAL_PROMPT"] = "0";
        if (!nonInteractive)
        {
            return startInfo;
        }

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
        return startInfo;
    }

    internal ProcessStartInfo CreateStartInfoForTests(string resolvedExecutable) =>
        CreateStartInfo(resolvedExecutable);

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

    private static async Task<ManifestOperationResult<GitProcessOutput>> RunProcessAsync(
        ProcessStartInfo startInfo,
        TimeSpan timeout,
        CancellationToken cancellationToken
    )
    {
        using var process = new Process { StartInfo = startInfo };
        if (!process.Start())
        {
            return ManifestOperationResult<GitProcessOutput>.Failure(
                "Unable to start the Git executable."
            );
        }
        process.StandardInput.Close();

        var standardOutput = ReadBoundedAsync(process.StandardOutput);
        var standardError = ReadBoundedAsync(process.StandardError);
        using var timeoutCancellation = new CancellationTokenSource(timeout);
        using var operationCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            timeoutCancellation.Token
        );
        try
        {
            await process.WaitForExitAsync(operationCancellation.Token);
        }
        catch (OperationCanceledException)
        {
            await StopAsync(process);
            await Task.WhenAll(standardOutput, standardError);
            return ManifestOperationResult<GitProcessOutput>.Failure(
                cancellationToken.IsCancellationRequested
                    ? "Git operation was canceled."
                    : $"Git operation timed out after {timeout.TotalSeconds:0} seconds."
            );
        }

        var output = new GitProcessOutput(await standardOutput, await standardError);
        return process.ExitCode == 0
            ? ManifestOperationResult<GitProcessOutput>.Success(output)
            : ManifestOperationResult<GitProcessOutput>.Failure(
                CreateExitCodeError(process.ExitCode, output)
            );
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
                "no credentials available"
            )
                ? "Git authentication failed. Configure the operating-system Git credential helper or SSH credentials for this source."
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

    private static bool ContainsAny(string value, params string[] fragments) =>
        fragments.Any(fragment => value.Contains(fragment, StringComparison.OrdinalIgnoreCase));

    private static async Task<string> ReadBoundedAsync(StreamReader reader)
    {
        var buffer = new char[4096];
        var captured = new StringBuilder(MaximumDiagnosticCharacters);
        while (true)
        {
            var charactersRead = await reader.ReadAsync(buffer.AsMemory());
            if (charactersRead == 0)
            {
                return captured.ToString();
            }

            var remainingCapacity = MaximumDiagnosticCharacters - captured.Length;
            if (remainingCapacity > 0)
            {
                captured.Append(buffer, 0, Math.Min(charactersRead, remainingCapacity));
            }
        }
    }

    private string? ResolveExecutable()
    {
        return ResolveExecutable(
            executable,
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
