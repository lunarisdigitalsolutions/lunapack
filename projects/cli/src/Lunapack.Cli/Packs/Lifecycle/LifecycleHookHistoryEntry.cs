namespace Lunapack.Cli.Packs.Lifecycle;

internal sealed record LifecycleHookHistoryEntry
{
    public const int CurrentVersion = 1;

    public required int Version { get; init; }

    public required DateTimeOffset Timestamp { get; init; }

    public required string Event { get; init; }

    public required string Executable { get; init; }

    public required string PackId { get; init; }

    public required string PackVersion { get; init; }

    public required IReadOnlyList<string> Arguments { get; init; }

    public required string AuthorizationSource { get; init; }

    public required IReadOnlyList<string> TrustScopes { get; init; }

    public required long DurationMilliseconds { get; init; }

    public required string Outcome { get; init; }

    public int? ExitCode { get; init; }
}
