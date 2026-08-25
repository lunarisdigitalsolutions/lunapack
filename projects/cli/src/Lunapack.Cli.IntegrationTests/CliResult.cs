namespace Lunapack.Cli.IntegrationTests;

internal sealed record CliResult(int ExitCode, string StandardOutput, string StandardError);
