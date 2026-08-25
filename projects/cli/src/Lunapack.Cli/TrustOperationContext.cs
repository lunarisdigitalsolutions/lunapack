namespace Lunapack.Cli;

internal sealed record TrustOperationContext(
    ProjectState State,
    UserSettings Settings,
    string ProjectKey
);
