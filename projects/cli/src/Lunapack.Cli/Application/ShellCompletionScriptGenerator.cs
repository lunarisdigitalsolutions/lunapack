namespace Lunapack.Cli.Application;

internal static class ShellCompletionScriptGenerator
{
    public static string? Generate(string? shell)
    {
        var selectedShell = shell ?? InferShell();
        return selectedShell switch
        {
            "bash" => """
                _luna_complete()
                {
                    local cur="${COMP_WORDS[COMP_CWORD]}" IFS=$'\n'
                    local candidates
                    read -d '' -ra candidates < <(luna complete --position "${COMP_POINT}" "${COMP_LINE}" 2>/dev/null)
                    read -d '' -ra COMPREPLY < <(compgen -W "${candidates[*]:-}" -- "$cur")
                }
                complete -f -F _luna_complete luna

                """,
            "fish" => """
                complete -f -c luna -a "(luna complete (commandline -cp))"

                """,
            "nushell" => """"
                def "nu-complete luna" [context: string] {
                    ^luna complete $"($context)" | lines
                }

                export extern "luna" [
                    ...command: string@"nu-complete luna"
                ]

                """",
            "pwsh" => """
                Register-ArgumentCompleter -Native -CommandName luna -ScriptBlock {
                    param($wordToComplete, $commandAst, $cursorPosition)
                    luna complete --position $cursorPosition "$commandAst" | ForEach-Object {
                        [System.Management.Automation.CompletionResult]::new($_, $_, 'ParameterValue', $_)
                    }
                }

                """,
            "zsh" => """
                _luna_complete()
                {
                    local completions=("$(luna complete "$words")")

                    if [ -z "$completions" ]
                    then
                        _arguments '*::arguments: _normal'
                        return
                    fi

                    _values = "${(ps:\n:)completions}"
                }
                compdef _luna_complete luna

                """,
            _ => null,
        };
    }

    private static string? InferShell()
    {
        if (OperatingSystem.IsWindows())
        {
            return "pwsh";
        }

        var shellPath = Environment.GetEnvironmentVariable("SHELL");
        return shellPath is null ? null : Path.GetFileName(shellPath);
    }
}
