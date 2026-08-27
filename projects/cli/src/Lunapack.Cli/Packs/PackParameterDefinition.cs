namespace Lunapack.Cli;

internal sealed record PackParameterDefinition(
    PackParameterType Type,
    bool Required,
    IReadOnlyList<string> Values,
    string? DisplayName = null,
    string? Description = null,
    object? Default = null
);
