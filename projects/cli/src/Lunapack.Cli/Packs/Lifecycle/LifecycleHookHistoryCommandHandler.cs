using System.CommandLine;
using System.Globalization;
using Lunapack.Cli.Application;

namespace Lunapack.Cli.Packs.Lifecycle;

internal sealed class LifecycleHookHistoryCommandHandler(
    LifecycleHookHistoryStore historyStore,
    CliConsole console
)
{
    private const int DefaultLimit = 20;

    public Command CreateCommand()
    {
        var hooksCommand = new Command("hooks", "Inspect local lifecycle hook activity.");
        hooksCommand.Subcommands.Add(CreateHistoryCommand());
        return hooksCommand;
    }

    private Command CreateHistoryCommand()
    {
        var limitOption = new Option<int?>("--limit")
        {
            Description =
                $"Maximum number of newest entries to display (1-100, default {DefaultLimit}).",
        };
        var command = new Command("history", "Show recent lifecycle script executions.")
        {
            limitOption,
        };
        command.SetAction(parseResult =>
            ExecuteAsync(parseResult.GetValue(limitOption) ?? DefaultLimit)
        );
        return command;
    }

    private async Task<int> ExecuteAsync(int limit)
    {
        if (limit is < 1 or > LifecycleHookHistoryStore.MaximumEntryCount)
        {
            return console.Fail(
                $"The history limit must be from 1 through {LifecycleHookHistoryStore.MaximumEntryCount}."
            );
        }

        var result = await historyStore.LoadAsync();
        if (result.Value is not { } entries)
        {
            return console.Fail(result.Error);
        }

        var selected = entries.Reverse().Take(limit).ToArray();
        if (selected.Length == 0)
        {
            console.Info("No lifecycle hook history has been recorded.");
            return 0;
        }

        console.Info("Lifecycle hook history (newest first):");
        foreach (var entry in selected)
        {
            Render(entry);
        }

        return 0;
    }

    private void Render(LifecycleHookHistoryEntry entry)
    {
        console.Info(
            $"{entry.Timestamp.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture)}  {entry.Event}  {entry.PackId}@{entry.PackVersion}"
        );
        console.Info($"  Outcome: {FormatOutcome(entry)}");
        console.Info($"  Executable: {entry.Executable}");
        console.Info($"  Arguments: {FormatArguments(entry.Arguments)}");
        console.Info($"  Authorization: {FormatAuthorization(entry)}");
    }

    private static string FormatOutcome(LifecycleHookHistoryEntry entry)
    {
        var exitCode = entry.ExitCode is { } value ? $", exit {value}" : string.Empty;
        return $"{entry.Outcome}, {entry.DurationMilliseconds} ms{exitCode}";
    }

    private static string FormatArguments(IReadOnlyList<string> arguments) =>
        arguments.Count == 0
            ? "-"
            : string.Join(
                " ",
                arguments.Select(argument =>
                    $"\"{argument.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal)}\""
                )
            );

    private static string FormatAuthorization(LifecycleHookHistoryEntry entry) =>
        entry.AuthorizationSource switch
        {
            "scripts-run" => "--scripts run",
            "persisted-trust" => $"persisted trust ({string.Join(", ", entry.TrustScopes)})",
            "interactive-confirmation" => "interactive confirmation",
            _ => entry.AuthorizationSource,
        };
}
