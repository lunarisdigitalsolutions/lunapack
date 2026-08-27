namespace Lunapack.Cli;

internal sealed record LinkOutdatedReport(string Name, IReadOnlyList<string> Reasons);
