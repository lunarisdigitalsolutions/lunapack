namespace Lunapack.Cli.Doctor;

internal sealed record DoctorReport(IReadOnlyList<DoctorCheckResult> Checks)
{
    public bool HasBlockingFailures => Checks.Any(check => check.IsBlocking);
}
