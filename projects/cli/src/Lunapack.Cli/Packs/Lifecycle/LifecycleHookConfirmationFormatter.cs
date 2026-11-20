using Lunapack.Cli.Application;

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
            ? $"{TerminalText.EncodeControls(invocation.Executable)} {TerminalText.EncodeControls(packedFile.RelativePath)}"
            : TerminalText.EncodeControls(invocation.Executable);
        var arguments = FormatArguments(invocation.Invocation.Script.Arguments);
        return arguments.Length == 0 ? command : $"{command} {arguments}";
    }

    private static string FormatArguments(List<string> arguments) =>
        string.Join(" ", arguments.Select(Escape));

    private static string Escape(string value) =>
        EscapeValue(value, TerminalText.EncodeControls(value));

    private static string EscapeValue(string value, string encodedValue) =>
        value.Any(char.IsWhiteSpace) || value.Contains('"')
            ? $"\"{encodedValue.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal)}\""
            : encodedValue;
}
