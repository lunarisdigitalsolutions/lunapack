using Lunapack.Cli.Project;

namespace Lunapack.Cli.Trust;

internal sealed record TrustOperationContext(
    ProjectState State,
    UserSettings Settings,
    string ProjectKey
);
