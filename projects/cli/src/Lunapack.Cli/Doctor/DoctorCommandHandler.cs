using System.CommandLine;
using Lunapack.Cli.Application;

namespace Lunapack.Cli.Doctor;

internal sealed class DoctorCommandHandler(
    IDoctorService doctorService,
    WorkspaceDirectoryResolver workspaceDirectoryResolver,
    CliConsole console
)
{
    public Command CreateCommand(string projectDirectory, Option<string?> workspaceOption)
    {
        var networkOption = new Option<bool>("--network")
        {
            Description = "Check configured Git and OCI sources with bounded network timeouts.",
        };
        var command = new Command(
            "doctor",
            "Check Git, credentials, state, and profile/cache directories."
        );
        command.Options.Add(networkOption);
        command.SetAction(async parseResult =>
            await RunAsync(
                workspaceDirectoryResolver.Resolve(
                    projectDirectory,
                    parseResult.GetValue(workspaceOption)
                ),
                parseResult.GetValue(networkOption)
            )
        );
        return command;
    }

    public async Task<int> RunAsync(string projectDirectory, bool checkNetwork)
    {
        var report = await doctorService.InspectAsync(projectDirectory, checkNetwork);
        foreach (var check in report.Checks)
        {
            var status = check.Status switch
            {
                DoctorCheckStatus.Pass => "PASS",
                DoctorCheckStatus.Fail => "FAIL",
                _ => "INFO",
            };
            console.Info(
                $"[{status}] {DoctorText.Sanitize(check.Name)}: {DoctorText.Sanitize(check.Message)}"
            );
            if (check.Remediation is not null)
            {
                console.Info($"  Remediation: {DoctorText.Sanitize(check.Remediation)}");
            }
        }

        return report.HasBlockingFailures ? 1 : 0;
    }
}
