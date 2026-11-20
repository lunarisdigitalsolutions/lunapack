namespace Lunapack.Cli.Sources.Git;

internal sealed record GitProcessOutput(
    string StandardOutput,
    string StandardError,
    bool StandardOutputTruncated = false,
    bool StandardErrorTruncated = false
);
