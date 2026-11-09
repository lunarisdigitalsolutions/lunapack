using System.IO.Abstractions;
using System.Security.Cryptography;
using System.Text;
using Lunapack.Cli.Application.CommandExecution;
using Lunapack.Cli.Application.Paths;
using Lunapack.Cli.Project;
using Lunapack.Cli.Sources;
using Lunapack.Cli.Trust;

namespace Lunapack.Cli.Sources.Oci;

internal static class OciSourceCredentialIdentity
{
    public static ManifestOperationResult<string> Create(
        IFileSystem fileSystem,
        string projectDirectory,
        ProjectConfiguration.OciSource source
    )
    {
        var projectKey = CanonicalProjectPath.Resolve(fileSystem, projectDirectory);
        if (projectKey.Value is not { } canonicalProject)
        {
            return ManifestOperationResult<string>.Failure(
                projectKey.Error ?? "Unable to identify the project for registry credentials."
            );
        }

        var normalizedSource = SourceIdentityNormalizer.CreateOci(source.Url, source.Path);
        if (normalizedSource.Value is not { } fingerprint)
        {
            return ManifestOperationResult<string>.Failure(
                normalizedSource.Error ?? "OCI source identity is invalid."
            );
        }

        var identity = string.Join(
            '\0',
            "lunapack-oci-credential-v1",
            canonicalProject,
            source.Name,
            fingerprint.Identity,
            fingerprint.Path ?? string.Empty
        );
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(identity));
        return ManifestOperationResult<string>.Success(
            $"lunapack-oci-v1:{Convert.ToHexString(digest).ToLowerInvariant()}"
        );
    }
}
