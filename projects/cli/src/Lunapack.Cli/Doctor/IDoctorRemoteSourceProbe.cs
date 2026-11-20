using Lunapack.Cli.Project;

namespace Lunapack.Cli.Doctor;

internal interface IDoctorRemoteSourceProbe
{
    Task<DoctorRemoteProbeResult> ProbeGitAsync(
        ProjectConfiguration.GitSource source,
        CancellationToken cancellationToken
    );

    Task<DoctorRemoteProbeResult> ProbeOciAsync(
        string projectDirectory,
        ProjectConfiguration.OciSource source,
        string? packId,
        CancellationToken cancellationToken
    );
}
