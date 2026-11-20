using Lunapack.Cli.Application.CommandExecution;

namespace Lunapack.Cli.Sources.Git;

internal interface IGitProcessRunner
{
    Task<ManifestOperationResult<GitProcessOutput>> RunAsync(
        IReadOnlyList<string> arguments,
        TimeSpan timeout,
        CancellationToken cancellationToken
    );

    Task<ManifestOperationResult<GitProcessOutput>> RunAsync(
        IReadOnlyList<string> arguments,
        TimeSpan timeout,
        GitTransportMode transportMode,
        CancellationToken cancellationToken
    ) => RunAsync(arguments, timeout, cancellationToken);

    Task<ManifestOperationResult<GitProcessOutput>> RunAsync(
        IReadOnlyList<string> arguments,
        TimeSpan timeout,
        GitTransportMode transportMode,
        string? repositoryUrl,
        CancellationToken cancellationToken
    ) => RunAsync(arguments, timeout, transportMode, cancellationToken);
}
