namespace Lunapack.Cli.Doctor;

internal interface IDoctorTemporaryFileProbe
{
    DoctorTemporaryFileProbeResult Probe(string directory);
}
