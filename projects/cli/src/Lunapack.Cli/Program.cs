using System.IO.Abstractions;
using Spectre.Console;

namespace Lunapack.Cli;

internal static class Program
{
    static async Task<int> Main(string[] args)
    {
        if (Packs.Lifecycle.LifecycleHookSupervisor.IsInvocation(args))
        {
            return await Packs.Lifecycle.LifecycleHookSupervisor.RunAsync(args, new FileSystem());
        }

        return await new CliApplication(new FileSystem(), AnsiConsole.Console).RunAsync(
            args,
            Environment.CurrentDirectory
        );
    }
}
