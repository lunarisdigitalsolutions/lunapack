namespace Lunapack.Cli;

internal static class LifecycleHookConfirmationFormatter
{
    public static string Format(ResolvedLifecycleHookInvocation invocation) =>
        string.Join(
            Environment.NewLine,
            $"Pack: {invocation.Invocation.Pack.Manifest.Id}@{invocation.Invocation.Pack.Manifest.Version}",
            $"Source: {FormatSource(invocation.Invocation.Pack.SourceIdentity)}",
            $"Hook: {LifecycleHookPlanner.ToManifestValue(invocation.Invocation.Hook)}",
            invocation.Invocation.Script.Description is { } description
                ? $"Description: {description}"
                : "Description: -",
            invocation.Invocation.PackedFile is { } packedFile
                ? $"Packed file: {packedFile.RelativePath}"
                : "Packed file: -",
            $"Executable: {invocation.Executable}",
            $"Arguments: {FormatArguments(invocation.Invocation.Script.Arguments)}"
        );

    private static string FormatArguments(List<string> arguments) =>
        arguments.Count == 0 ? "-" : string.Join(" ", arguments.Select(Escape));

    private static string Escape(string value) =>
        value.Any(char.IsWhiteSpace) || value.Contains('"')
            ? $"\"{value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal)}\""
            : value;

    private static string FormatSource(ConfiguredSourceIdentity source) =>
        source.Type switch
        {
            "local" => $"local:{source.Path}",
            "git" => $"git:{source.Url}#{source.Ref ?? "HEAD"}/{source.Path ?? "."}",
            _ => source.Type,
        };
}
