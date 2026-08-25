namespace Lunapack.Cli;

internal interface IGitProcessRunner
{
    Task<ManifestOperationResult<GitProcessOutput>> RunAsync(
        IReadOnlyList<string> arguments,
        TimeSpan timeout,
        CancellationToken cancellationToken
    );
}
