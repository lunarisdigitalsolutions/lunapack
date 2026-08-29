using System.Diagnostics;

namespace Lunapack.Cli.IntegrationTests;

internal static class GitProcess
{
    public static async Task<string> InvokeAsync(string workingDirectory, params string[] arguments)
    {
        var startInfo = new ProcessStartInfo("git")
        {
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            WorkingDirectory = workingDirectory,
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process =
            Process.Start(startInfo) ?? throw new InvalidOperationException("Unable to start Git.");
        var standardOutput = process.StandardOutput.ReadToEndAsync();
        var standardError = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"Git failed with code {process.ExitCode}: {await standardError}"
            );
        }

        return await standardOutput;
    }
}
