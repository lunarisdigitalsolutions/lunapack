using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using Lunapack.Cli.Application.CommandExecution;

namespace Lunapack.Cli.Sources.Git;

internal sealed class GitProcessRunner(string executable = "git") : IGitProcessRunner
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

        var startInfo = new ProcessStartInfo(executable)
        {
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };
        startInfo.Environment["GIT_TERMINAL_PROMPT"] = "0";
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        try
        {
            using var process = new Process { StartInfo = startInfo };
            if (!process.Start())
            {
                return ManifestOperationResult<GitProcessOutput>.Failure(
                    "Unable to start the Git executable."
                );
            }

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
        catch (Win32Exception exception)
        {
            return ManifestOperationResult<GitProcessOutput>.Failure(
                $"Unable to run Git executable '{executable}': {exception.Message}"
            );
        }
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
