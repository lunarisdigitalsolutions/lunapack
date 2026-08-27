using System.ComponentModel;
using System.Diagnostics;
using System.IO.Abstractions;
using System.Text;

namespace Lunapack.Cli;

internal sealed class LifecycleHookExecutor(IFileSystem fileSystem, CliConsole console)
{
    private const int MaximumOutputCharacters = 64 * 1024;

    public async Task<ManifestOperationResult<bool>> ExecuteAsync(
        string projectDirectory,
        ResolvedLifecycleHookInvocation invocation,
        CancellationToken cancellationToken = default
    )
    {
        var integrity = VerifyPackedFile(invocation);
        if (!integrity.IsSuccess)
        {
            return integrity;
        }

        try
        {
            using var process = new Process
            {
                StartInfo = CreateStartInfo(projectDirectory, invocation, console.IsInteractive),
            };
            if (!process.Start())
            {
                return ManifestOperationResult<bool>.Failure(
                    $"Unable to start lifecycle hook for pack '{invocation.Invocation.Pack.Manifest.Id}'."
                );
            }

            var standardOutput = process.StartInfo.RedirectStandardOutput
                ? ReadBoundedAsync(process.StandardOutput)
                : Task.FromResult(string.Empty);
            var standardError = process.StartInfo.RedirectStandardError
                ? ReadBoundedAsync(process.StandardError)
                : Task.FromResult(string.Empty);
            try
            {
                await process.WaitForExitAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                await StopAsync(process);
                return ManifestOperationResult<bool>.Failure(
                    "Lifecycle hook execution was canceled."
                );
            }

            WriteOutput(await standardOutput, isError: false);
            WriteOutput(await standardError, isError: true);
            return process.ExitCode == 0
                ? ManifestOperationResult<bool>.Success(true)
                : ManifestOperationResult<bool>.Failure(
                    $"Lifecycle hook '{LifecycleHookPlanner.ToManifestValue(invocation.Invocation.Hook)}' for pack '{invocation.Invocation.Pack.Manifest.Id}' exited with code {process.ExitCode}."
                );
        }
        catch (Win32Exception exception)
        {
            return ManifestOperationResult<bool>.Failure(
                $"Unable to start lifecycle hook executable '{invocation.Executable}': {exception.Message}"
            );
        }
    }

    private ManifestOperationResult<bool> VerifyPackedFile(
        ResolvedLifecycleHookInvocation invocation
    )
    {
        if (invocation.Invocation.PackedFile is not { } packedFile)
        {
            return ManifestOperationResult<bool>.Success(true);
        }

        var verified = packedFile.Verify(fileSystem);
        return verified.IsSuccess
            ? ManifestOperationResult<bool>.Success(true)
            : ManifestOperationResult<bool>.Failure(
                verified.Error ?? "Packed lifecycle hook file integrity verification failed."
            );
    }

    internal static ProcessStartInfo CreateStartInfo(
        string projectDirectory,
        ResolvedLifecycleHookInvocation invocation,
        bool isInteractive
    )
    {
        var startInfo = new ProcessStartInfo(invocation.Executable)
        {
            CreateNoWindow = !isInteractive,
            RedirectStandardError = !isInteractive,
            RedirectStandardOutput = !isInteractive,
            UseShellExecute = false,
            WorkingDirectory = projectDirectory,
        };
        foreach (var argument in invocation.Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        return startInfo;
    }

    private void WriteOutput(string output, bool isError)
    {
        var sanitized = Sanitize(output);
        if (sanitized.Length > 0)
        {
            if (isError)
            {
                console.Warning(sanitized);
            }
            else
            {
                console.Info(sanitized);
            }
        }
    }

    private static async Task<string> ReadBoundedAsync(StreamReader reader)
    {
        var buffer = new char[4096];
        var output = new StringBuilder(MaximumOutputCharacters);
        while (true)
        {
            var read = await reader.ReadAsync(buffer.AsMemory());
            if (read == 0)
            {
                return output.ToString();
            }

            output.Append(buffer, 0, Math.Min(read, MaximumOutputCharacters - output.Length));
        }
    }

    private static string Sanitize(string output) =>
        new string(
            output
                .Where(character => character is '\n' or '\r' or '\t' || !char.IsControl(character))
                .ToArray()
        );

    private static async Task StopAsync(Process process)
    {
        if (!process.HasExited)
        {
            process.Kill(entireProcessTree: true);
        }

        await process.WaitForExitAsync();
    }
}
