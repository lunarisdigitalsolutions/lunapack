using System.IO.Abstractions;
using Spectre.Console;

namespace Lunapack.Cli;

internal static class Program
{
    static async Task<int> Main(string[] args) =>
        await new CliApplication(new FileSystem(), AnsiConsole.Console).RunAsync(
            args,
            Environment.CurrentDirectory
        );
}
