namespace Lunapack.Cli.Links;

internal sealed record LinkOutdatedReport(string Name, IReadOnlyList<string> Reasons);
