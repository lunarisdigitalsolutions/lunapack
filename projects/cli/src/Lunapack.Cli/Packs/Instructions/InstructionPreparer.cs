using System.IO.Abstractions;
using System.Text;
using Lunapack.Cli.Application.CommandExecution;
using Lunapack.Cli.Catalog;
using Lunapack.Cli.Packs.Lifecycle;
using Lunapack.Cli.Packs.Manifest;

namespace Lunapack.Cli.Packs.Instructions;

internal sealed class InstructionPreparer(IFileSystem fileSystem)
{
    private static readonly UTF8Encoding _utf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true
    );

    private readonly PackTemplateRenderer _templateRenderer = new(fileSystem);

    public ManifestOperationResult<PreparedInstruction> Prepare(
        DiscoveredPack pack,
        PackManifest.PackHook hook,
        ResolvedPackParameters parameters
    )
    {
        var resolvedFile = PackedHookFile.Resolve(fileSystem, pack, hook.File);
        if (resolvedFile.Value is not { } packedFile)
        {
            return ManifestOperationResult<PreparedInstruction>.Failure(
                resolvedFile.Error ?? "Unable to bind packed instruction file."
            );
        }

        var templating = hook.Templating ?? false;
        var rendered = _templateRenderer.Render(packedFile.CanonicalPath, templating, parameters);
        if (rendered.Value is not { } contents)
        {
            return ManifestOperationResult<PreparedInstruction>.Failure(
                rendered.Error ?? $"Instruction '{packedFile.RelativePath}' cannot be prepared."
            );
        }

        try
        {
            return ManifestOperationResult<PreparedInstruction>.Success(
                new PreparedInstruction(
                    packedFile,
                    templating,
                    InstructionParser.Parse(_utf8.GetString(contents))
                )
            );
        }
        catch (DecoderFallbackException exception)
        {
            return ManifestOperationResult<PreparedInstruction>.Failure(
                $"Instruction '{packedFile.RelativePath}' is not valid UTF-8: {exception.Message}"
            );
        }
    }
}
