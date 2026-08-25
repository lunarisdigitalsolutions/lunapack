using System.CommandLine;

namespace Lunapack.Cli;

internal sealed class ValidatePackCommandHandler(
    PackValidationService packValidationService,
    WorkspaceDirectoryResolver workspaceDirectoryResolver,
    CliConsole console
)
{
    public Command CreateCommand(string projectDirectory, Option<string?> workspaceOption)
    {
        var packReferenceArgument = new Argument<string>("pack-reference")
        {
            Description = "Pack ID, optionally followed by @version.",
        };
        var command = new Command("validate", "Validate a pack in configured sources.")
        {
            packReferenceArgument,
        };
        command.SetAction(async parseResult =>
        {
            var packReferenceValue = parseResult.GetValue(packReferenceArgument);
            if (packReferenceValue is null)
            {
                return console.Fail("A pack ID is required.");
            }

            var packReference = PackReference.Parse(packReferenceValue);
            if (packReference.Value is not { } reference)
            {
                return console.Fail(packReference.Error);
            }

            var result = await packValidationService.ValidateAsync(
                workspaceDirectoryResolver.Resolve(
                    projectDirectory,
                    parseResult.GetValue(workspaceOption)
                ),
                reference.Id,
                reference.Version
            );
            if (result.Value is not { } validation)
            {
                return console.Fail(result.Error);
            }

            var manifest = validation.Manifest;
            if (!validation.IsValid || manifest is null)
            {
                foreach (var issue in validation.Issues)
                {
                    console.Error(issue);
                }

                return 1;
            }

            console.Info($"{manifest.Id}@{manifest.Version} is valid.");
            return 0;
        });

        return command;
    }
}
