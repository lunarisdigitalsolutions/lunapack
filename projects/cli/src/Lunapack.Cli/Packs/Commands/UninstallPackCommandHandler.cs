using System.CommandLine;

namespace Lunapack.Cli;

internal sealed class UninstallPackCommandHandler(
    PackLifecycleService packLifecycleService,
    WorkspaceDirectoryResolver workspaceDirectoryResolver,
    CliConsole console
)
{
    public Command CreateCommand(string projectDirectory, Option<string?> workspaceOption)
    {
        var packReferenceArgument = new Argument<string[]>("pack-reference")
        {
            Arity = ArgumentArity.OneOrMore,
            Description = "Pack IDs, optionally followed by @version.",
        };
        var command = new Command("uninstall", "Remove an installed pack.")
        {
            packReferenceArgument,
        };
        command.SetAction(async parseResult =>
        {
            var packReferenceValues = parseResult.GetValue(packReferenceArgument) ?? [];
            if (packReferenceValues.Length == 0)
            {
                return console.Fail("A pack ID is required.");
            }

            var workspaceDirectory = workspaceDirectoryResolver.Resolve(
                projectDirectory,
                parseResult.GetValue(workspaceOption)
            );
            foreach (var packReferenceValue in packReferenceValues)
            {
                var packReference = PackReference.Parse(packReferenceValue);
                if (packReference.Value is not { } reference)
                {
                    return console.Fail(packReference.Error);
                }

                var exitCode = await console.RunWithStatusAsync(
                    $"Uninstalling {reference.Id}...",
                    () => packLifecycleService.UninstallAsync(workspaceDirectory, reference)
                );
                if (exitCode != 0)
                {
                    return exitCode;
                }
            }

            return 0;
        });

        return command;
    }
}
