using Lunapack.Cli.Application;
using Lunapack.Cli.Application.CommandExecution;
using Lunapack.Cli.Project;

namespace Lunapack.Cli.Sources.Oci;

internal sealed class ConsoleOciCredentialPrompt(CliConsole console) : IOciCredentialPrompt
{
    public ManifestOperationResult<OciRegistryCredential?> Prompt(
        ProjectConfiguration.OciSource source
    )
    {
        if (!console.IsInteractive)
        {
            return ManifestOperationResult<OciRegistryCredential?>.Success(null);
        }

        var location = SourceIdentityNormalizer.NormalizeOciLocation(source.Url, source.Path);
        if (!location.IsSuccess)
        {
            return ManifestOperationResult<OciRegistryCredential?>.Failure(
                location.Error ?? $"OCI source '{source.Name}' is invalid."
            );
        }

        var path = string.IsNullOrEmpty(location.Value.Path)
            ? string.Empty
            : $"/{location.Value.Path}";
        console.Info(
            $"Authentication required for source '{source.Name}' at {location.Value.Url}{path}."
        );
        var username = string.IsNullOrWhiteSpace(source.Username)
            ? console.PromptText("Registry username:")
            : source.Username;
        if (string.IsNullOrWhiteSpace(username))
        {
            return ManifestOperationResult<OciRegistryCredential?>.Success(null);
        }

        var password = console.PromptSecret("Registry password or token:");
        return string.IsNullOrEmpty(password)
            ? ManifestOperationResult<OciRegistryCredential?>.Success(null)
            : ManifestOperationResult<OciRegistryCredential?>.Success(
                new OciRegistryCredential(username, password)
            );
    }
}
