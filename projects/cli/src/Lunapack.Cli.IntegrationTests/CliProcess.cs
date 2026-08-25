using System.Diagnostics;

namespace Lunapack.Cli.IntegrationTests;

internal static class CliProcess
{
    public static async Task<CliResult> InvokeAsync(
        string workingDirectory,
        params string[] arguments
    ) => await InvokeCoreAsync(workingDirectory, arguments);

    private static async Task<CliResult> InvokeCoreAsync(
        string workingDirectory,
        IReadOnlyList<string> arguments
    )
    {
        var cliAssemblyPath = Path.Combine(AppContext.BaseDirectory, "luna.dll");
        var startInfo = new ProcessStartInfo("dotnet")
        {
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            WorkingDirectory = workingDirectory,
        };
        startInfo.Environment["NO_COLOR"] = "1";

        startInfo.ArgumentList.Add(cliAssemblyPath);
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process =
            Process.Start(startInfo)
            ?? throw new InvalidOperationException("Unable to start the LunaPack CLI process.");
        var standardOutput = process.StandardOutput.ReadToEndAsync();
        var standardError = process.StandardError.ReadToEndAsync();

        await process.WaitForExitAsync();

        return new CliResult(process.ExitCode, await standardOutput, await standardError);
    }
}
