using Lunapack.Cli.Application.CommandExecution;

namespace Lunapack.Cli.Packs;

internal sealed record PackReference(string Id, string? Version)
{
    public static ManifestOperationResult<PackReference> Parse(string value)
    {
        var versionSeparator = value.LastIndexOf('@');
        if (versionSeparator < 0)
        {
            return ManifestOperationResult<PackReference>.Success(new PackReference(value, null));
        }

        var hasPackId = versionSeparator > 0;
        var hasVersion = versionSeparator < value.Length - 1;
        if (!hasPackId || !hasVersion)
        {
            return ManifestOperationResult<PackReference>.Failure(
                $"Pack reference '{value}' must use the form id@version."
            );
        }

        return ManifestOperationResult<PackReference>.Success(
            new PackReference(value[..versionSeparator], value[(versionSeparator + 1)..])
        );
    }
}
