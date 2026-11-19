using System.CommandLine;
using Lunapack.Cli.Application;
using Lunapack.Cli.Application.Diagnostics;
using Lunapack.Cli.Packs.Validation;

namespace Lunapack.Cli.Packs.Commands;

internal sealed class ValidatePackCommandHandler(
    PackValidationService packValidationService,
    CliCompletionProvider completionProvider,
    WorkspaceDirectoryResolver workspaceDirectoryResolver,
    CliConsole console
)
{
    public Command CreateCommand(string projectDirectory, Option<string?> workspaceOption)
    {
        var packReferenceArgument = new Argument<string>("pack-reference")
        {
            Description = "Pack ID, optionally followed by @version.",
            HelpName = "pack-reference",
        };
        packReferenceArgument.CompletionSources.Add(completionProvider.GetAvailablePackIds);
        var command = new Command("validate", "Validate a pack in configured sources.")
        {
            packReferenceArgument,
        };
        var formatOption = CliOutputFormatOption.Create();
        command.Options.Add(formatOption);
        command.SetAction(async parseResult =>
        {
            if (!CliOutputFormatOption.TryParse(parseResult.GetValue(formatOption), out var format))
            {
                return console.Fail(
                    CliOutputFormatOption.FormatUnsupportedMessage(
                        parseResult.GetValue(formatOption)
                    )
                );
            }

            var output = parseResult.InvocationConfiguration.Output;
            var packReferenceValue = parseResult.GetValue(packReferenceArgument);
            if (packReferenceValue is null)
            {
                return Fail("A pack ID is required.", format, output);
            }

            var packReference = PackReference.Parse(packReferenceValue);
            if (packReference.Value is not { } reference)
            {
                return Fail(packReference.Error, format, output);
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
                return Fail(result.Error, format, output);
            }

            var manifest = validation.Manifest;
            if (!validation.IsValid || manifest is null)
            {
                foreach (var issue in validation.Issues)
                {
                    console.Error(issue);
                }

                if (format == CliOutputFormat.Json)
                {
                    CliJsonWriter.WriteValidation(output, new CliJsonValidationResult(false));
                }

                return 1;
            }

            if (format == CliOutputFormat.Json)
            {
                CliJsonWriter.WriteValidation(output, new CliJsonValidationResult(true));
                return 0;
            }

            console.Info($"{manifest.Id}@{manifest.Version} is valid.");
            return 0;
        });

        return command;
    }

    private int Fail(string? message, CliOutputFormat format, TextWriter output)
    {
        var exitCode = console.Fail(message);
        if (format == CliOutputFormat.Json)
        {
            CliJsonWriter.WriteValidation(output, null);
        }

        return exitCode;
    }
}
