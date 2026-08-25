namespace Lunapack.Cli;

internal static class TrustOutputFormatter
{
    public static IReadOnlyList<string> Format(TrustListing listing)
    {
        var lines = new List<string>();
        var scope = FormatScope(listing.Scope);
        lines.AddRange(listing.Sources.Select(source => $"{scope} source - {Format(source)}"));
        lines.AddRange(
            listing.Packs.Select(pack =>
                $"{scope} pack - id: {Escape(pack.Id)} - {Format(pack.Source)}"
            )
        );
        lines.AddRange(
            listing.ProjectSourceDeclarations.Select(name =>
                $"project source declaration - name: {Escape(name)}"
            )
        );
        lines.AddRange(
            listing.ProjectPackDeclarations.Select(pack =>
                $"project pack declaration - source: {Escape(pack.Source)} - id: {Escape(pack.Id)}"
            )
        );
        lines.AddRange(
            listing.ProjectSourceAcknowledgements.Select(source =>
                $"project source acknowledgement - {Format(source)}"
            )
        );
        lines.AddRange(
            listing.ProjectPackAcknowledgements.Select(pack =>
                $"project pack acknowledgement - id: {Escape(pack.Id)} - {Format(pack.Source)}"
            )
        );
        return lines.Count == 0 ? [$"No {scope} trust entries."] : lines;
    }

    private static string FormatScope(TrustScope scope) =>
        scope switch
        {
            TrustScope.LocalUser => "local-user",
            TrustScope.Project => "project",
            TrustScope.GlobalUser => "global-user",
            _ => throw new ArgumentOutOfRangeException(nameof(scope)),
        };

    private static string Format(ConfiguredSourceIdentity identity) =>
        identity.Type switch
        {
            "local" => $"identity: local(path={Escape(identity.Path)})",
            "git" =>
                $"identity: git(url={Escape(identity.Url)}, ref={Escape(identity.Ref ?? "<default>")}, path={Escape(identity.Path ?? "<root>")})",
            _ => throw new ArgumentOutOfRangeException(nameof(identity)),
        };

    private static string Escape(string? value) =>
        (value ?? string.Empty)
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\r", "\\r", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal);
}
