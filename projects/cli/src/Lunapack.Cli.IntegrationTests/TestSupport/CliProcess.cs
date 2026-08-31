using System.Diagnostics;

namespace Lunapack.Cli.IntegrationTests;

internal static class CliProcess
{
    public static async Task<CliResult> InvokeAsync(
        string workingDirectory,
        params string[] arguments
    ) => await InvokeCoreAsync(workingDirectory, arguments);

    public static async Task<CliResult> InvokeAsync(
        string workingDirectory,
        IReadOnlyDictionary<string, string?> environment,
        params string[] arguments
    ) => await InvokeCoreAsync(workingDirectory, arguments, environment);

    private static async Task<CliResult> InvokeCoreAsync(
        string workingDirectory,
        IReadOnlyList<string> arguments,
        IReadOnlyDictionary<string, string?>? environment = null
    )
    {
        var cliAssemblyPath = Path.Combine(AppContext.BaseDirectory, "luna.dll");
        var startInfo = new ProcessStartInfo("dotnet")
        {
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            WorkingDirectory = workingDirectory,
        };
        startInfo.Environment["NO_COLOR"] = "1";
        if (environment is not null)
        {
            foreach (var entry in environment)
            {
                startInfo.Environment[entry.Key] = entry.Value;
            }
        }

        startInfo.ArgumentList.Add(cliAssemblyPath);
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process =
            Process.Start(startInfo)
            ?? throw new InvalidOperationException("Unable to start the LunaPack CLI process.");
        process.StandardInput.Close();
        var standardOutput = process.StandardOutput.ReadToEndAsync();
        var standardError = process.StandardError.ReadToEndAsync();

        await process.WaitForExitAsync();

        return new CliResult(process.ExitCode, await standardOutput, await standardError);
    }
}
