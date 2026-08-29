namespace Lunapack.Cli.Packs.Lifecycle;

internal static class LifecycleHookConfirmationFormatter
{
    public static string Format(ResolvedLifecycleHookInvocation invocation) =>
        invocation.Invocation.Script.Description is { } description
            ? string.Join(Environment.NewLine, FormatCommand(invocation), description)
            : FormatCommand(invocation);

    public static string FormatCommand(ResolvedLifecycleHookInvocation invocation)
    {
        var command = invocation.Invocation.PackedFile is { } packedFile
            ? $"{invocation.Executable} {packedFile.RelativePath}"
            : invocation.Executable;
        var arguments = FormatArguments(invocation.Invocation.Script.Arguments);
        return arguments.Length == 0 ? command : $"{command} {arguments}";
    }

    private static string FormatArguments(List<string> arguments) =>
        string.Join(" ", arguments.Select(Escape));

    private static string Escape(string value) =>
        value.Any(char.IsWhiteSpace) || value.Contains('"')
            ? $"\"{value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal)}\""
            : value;
}
