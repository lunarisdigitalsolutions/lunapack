namespace Lunapack.Cli.Doctor;

internal sealed record DoctorCheckResult(
    string Name,
    DoctorCheckStatus Status,
    string Message,
    string? Remediation = null
)
{
    public bool IsBlocking => Status == DoctorCheckStatus.Fail;
}
