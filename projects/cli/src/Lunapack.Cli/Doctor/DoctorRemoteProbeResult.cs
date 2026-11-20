namespace Lunapack.Cli.Doctor;

internal sealed record DoctorRemoteProbeResult(
    DoctorRemoteProbeOutcome Outcome,
    string Message,
    string? Remediation = null
);
