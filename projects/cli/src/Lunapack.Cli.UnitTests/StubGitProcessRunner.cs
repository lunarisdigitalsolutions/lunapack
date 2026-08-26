namespace Lunapack.Cli.UnitTests;

internal sealed class StubGitProcessRunner(string? lsRemoteOutput = null) : IGitProcessRunner
{
    private readonly string _lsRemoteOutput =
        lsRemoteOutput
        ?? string.Join(
            '\n',
            "1111111111111111111111111111111111111111\trefs/heads/main",
            "2222222222222222222222222222222222222222\trefs/tags/v1.0.0"
        );

    public List<IReadOnlyList<string>> Invocations { get; } = [];

    public Task<ManifestOperationResult<GitProcessOutput>> RunAsync(
        IReadOnlyList<string> arguments,
        TimeSpan timeout,
        CancellationToken cancellationToken
    )
    {
        Invocations.Add(arguments);
        return Task.FromResult(
            arguments.Count > 0
            && string.Equals(arguments[0], "ls-remote", StringComparison.Ordinal)
                ? ManifestOperationResult<GitProcessOutput>.Success(
                    new GitProcessOutput(Filter(arguments), string.Empty)
                )
                : ManifestOperationResult<GitProcessOutput>.Failure(
                    "Git command is unavailable in tests."
                )
        );
    }

    private string Filter(IReadOnlyList<string> arguments)
    {
        if (arguments.Count < 4)
        {
            return _lsRemoteOutput;
        }

        var pattern = arguments[^1];
        var suffix = "/" + pattern;
        return string.Join(
            '\n',
            _lsRemoteOutput
                .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Where(line =>
                    line.EndsWith(suffix, StringComparison.Ordinal)
                    || line.EndsWith("\t" + pattern, StringComparison.Ordinal)
                )
        );
    }
}
