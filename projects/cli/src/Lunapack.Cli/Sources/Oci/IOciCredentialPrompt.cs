using Lunapack.Cli.Application.CommandExecution;
using Lunapack.Cli.Project;

namespace Lunapack.Cli.Sources.Oci;

internal interface IOciCredentialPrompt
{
    ManifestOperationResult<OciRegistryCredential?> Prompt(ProjectConfiguration.OciSource source);
}
