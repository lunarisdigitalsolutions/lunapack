namespace Lunapack.Cli.Doctor;

internal interface IDoctorService
{
    Task<DoctorReport> InspectAsync(
        string projectDirectory,
        bool checkNetwork,
        CancellationToken cancellationToken = default
    );
}
