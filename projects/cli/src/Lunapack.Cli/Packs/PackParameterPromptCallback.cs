namespace Lunapack.Cli.Packs;

internal delegate IReadOnlyDictionary<string, IReadOnlyList<string>> PackParameterPromptCallback(
    IReadOnlyList<PackParameterPrompt> prompts
);
