using Lunapack.Cli.Application.CommandExecution;

namespace Lunapack.Cli.Sources.Git;

internal interface IGitProcessRunner
{
    Task<ManifestOperationResult<GitProcessOutput>> RunAsync(
        IReadOnlyList<string> arguments,
        TimeSpan timeout,
        CancellationToken cancellationToken
    );
}
