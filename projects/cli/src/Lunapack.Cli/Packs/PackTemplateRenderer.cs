using System.IO.Abstractions;
using System.Text;
using Scriban;
using Scriban.Runtime;
using Scriban.Syntax;

namespace Lunapack.Cli;

internal sealed class PackTemplateRenderer(IFileSystem fileSystem)
{
    private static readonly UTF8Encoding _utf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true
    );

    public ManifestOperationResult<byte[]> Render(
        string templatePath,
        bool isTemplate,
        ResolvedPackParameters parameters
    )
    {
        try
        {
            var contents = fileSystem.File.ReadAllBytes(templatePath);
            if (!isTemplate)
            {
                return ManifestOperationResult<byte[]>.Success(contents);
            }

            var templateText = _utf8.GetString(contents);
            var rendered = RenderText(templateText, templatePath, parameters);
            return rendered.Value is { } value
                ? ManifestOperationResult<byte[]>.Success(_utf8.GetBytes(value))
                : ManifestOperationResult<byte[]>.Failure(
                    rendered.Error ?? $"Template '{templatePath}' cannot be rendered."
                );
        }
        catch (Exception exception)
            when (exception
                    is DecoderFallbackException
                        or IOException
                        or UnauthorizedAccessException
            )
        {
            return ManifestOperationResult<byte[]>.Failure(
                $"Template '{templatePath}' cannot be rendered: {exception.Message}"
            );
        }
    }

    public static ManifestOperationResult<string> RenderText(
        string templateText,
        string templateName,
        ResolvedPackParameters parameters
    )
    {
        try
        {
            var template = Template.Parse(templateText, templateName);
            if (template.HasErrors)
            {
                return ManifestOperationResult<string>.Failure(
                    $"Template '{templateName}' cannot be parsed: {string.Join(Environment.NewLine, template.Messages)}"
                );
            }

            var context = CreateContext(parameters);
            return ManifestOperationResult<string>.Success(template.Render(context));
        }
        catch (ScriptRuntimeException exception)
        {
            return ManifestOperationResult<string>.Failure(
                $"Template '{templateName}' cannot be rendered: {exception.Message}"
            );
        }
    }

    private static TemplateContext CreateContext(ResolvedPackParameters parameters)
    {
        var context = new TemplateContext(StringComparer.Ordinal) { StrictVariables = true };
        var globals = new ScriptObject(StringComparer.Ordinal);
        foreach (var (name, value) in parameters.Values)
        {
            globals.SetValue(name, value.Value, readOnly: true);
        }

        context.PushGlobal(globals);
        return context;
    }
}
