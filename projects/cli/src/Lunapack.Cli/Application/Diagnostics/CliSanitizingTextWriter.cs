using System.Text;

namespace Lunapack.Cli.Application.Diagnostics;

internal sealed class CliSanitizingTextWriter(TextWriter writer) : TextWriter
{
    public override Encoding Encoding => writer.Encoding;

    public override void Write(char value) =>
        writer.Write(
            value is '\r' or '\n'
                ? value.ToString()
                : CliTerminalSanitizer.Sanitize(value.ToString())
        );

    public override void Write(string? value)
    {
        if (value is not null)
        {
            writer.Write(CliTerminalSanitizer.SanitizeWriterChunk(value));
        }
    }

    public override void Write(char[] buffer, int index, int count) =>
        Write(new string(buffer, index, count));

    public override void Write(ReadOnlySpan<char> buffer) => Write(buffer.ToString());

    public override void WriteLine() => writer.WriteLine();

    public override void WriteLine(string? value) =>
        writer.WriteLine(value is null ? null : CliTerminalSanitizer.Sanitize(value));

    public override Task WriteAsync(char value) =>
        writer.WriteAsync(
            value is '\r' or '\n'
                ? value.ToString()
                : CliTerminalSanitizer.Sanitize(value.ToString())
        );

    public override Task WriteAsync(string? value) =>
        value is null
            ? Task.CompletedTask
            : writer.WriteAsync(CliTerminalSanitizer.SanitizeWriterChunk(value));

    public override Task WriteLineAsync(string? value) =>
        writer.WriteLineAsync(value is null ? null : CliTerminalSanitizer.Sanitize(value));

    public override void Flush() => writer.Flush();

    public override Task FlushAsync() => writer.FlushAsync();
}
