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

    public ManifestOperationResult<RenderedManagedFileTemplate> RenderManagedFile(
        string templatePath,
        bool isTemplate,
        ResolvedPackParameters parameters,
        ManagedFileTemplateContext managedFileContext
    )
    {
        try
        {
            var contents = fileSystem.File.ReadAllBytes(templatePath);
            if (!isTemplate)
            {
                return ManifestOperationResult<RenderedManagedFileTemplate>.Success(
                    new RenderedManagedFileTemplate(contents, [])
                );
            }

            var templateText = _utf8.GetString(contents);
            var diagnostics = new List<ManagedFileTemplateDiagnostic>();
            var rendered = RenderText(
                templateText,
                templatePath,
                parameters,
                managedFileContext,
                diagnostics
            );
            return rendered.Value is { } value
                ? ManifestOperationResult<RenderedManagedFileTemplate>.Success(
                    new RenderedManagedFileTemplate(_utf8.GetBytes(value), diagnostics)
                )
                : ManifestOperationResult<RenderedManagedFileTemplate>.Failure(
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
            return ManifestOperationResult<RenderedManagedFileTemplate>.Failure(
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

            var context = CreateContext(parameters, null, null);
            return ManifestOperationResult<string>.Success(template.Render(context));
        }
        catch (ScriptRuntimeException exception)
        {
            return ManifestOperationResult<string>.Failure(
                $"Template '{templateName}' cannot be rendered: {exception.Message}"
            );
        }
    }

    private static ManifestOperationResult<string> RenderText(
        string templateText,
        string templateName,
        ResolvedPackParameters parameters,
        ManagedFileTemplateContext managedFileContext,
        ICollection<ManagedFileTemplateDiagnostic> diagnostics
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

            var context = CreateContext(parameters, managedFileContext, diagnostics);
            return ManifestOperationResult<string>.Success(template.Render(context));
        }
        catch (ScriptRuntimeException exception)
        {
            return ManifestOperationResult<string>.Failure(
                $"Template '{templateName}' cannot be rendered: {exception.Message}"
            );
        }
    }

    private static TemplateContext CreateContext(
        ResolvedPackParameters parameters,
        ManagedFileTemplateContext? managedFileContext,
        ICollection<ManagedFileTemplateDiagnostic>? diagnostics
    )
    {
        var context = new TemplateContext(StringComparer.Ordinal) { StrictVariables = true };
        var globals = new ScriptObject(StringComparer.Ordinal);
        foreach (var (name, value) in parameters.Values)
        {
            globals.SetValue(
                name,
                value.StringValues is { } stringValues
                    ? new ScribanMultiSelectArray(stringValues)
                    : value.Value,
                readOnly: true
            );
        }

        if (
            parameters.Values.Values.Any(value => value.StringValues is not null)
            && !parameters.Values.ContainsKey("contains")
        )
        {
            globals.SetValue("contains", true, readOnly: true);
        }

        if (managedFileContext is not null && diagnostics is not null)
        {
            var files = new ScriptObject(StringComparer.Ordinal);
            files.SetValue(
                "path",
                DelegateCustomFunction.CreateFunc<string, string>(declaredTarget =>
                    ResolvePath(declaredTarget, managedFileContext, diagnostics)
                ),
                readOnly: true
            );
            files.SetValue(
                "relative_path",
                DelegateCustomFunction.CreateFunc<string, string>(declaredTarget =>
                    ResolveRelativePath(declaredTarget, managedFileContext, diagnostics)
                ),
                readOnly: true
            );
            globals.SetValue("files", files, readOnly: true);
        }

        context.PushGlobal(globals);
        return context;
    }

    private static string ResolvePath(
        string declaredTarget,
        ManagedFileTemplateContext context,
        ICollection<ManagedFileTemplateDiagnostic> diagnostics
    )
    {
        if (context.TryResolve(declaredTarget, out var effectiveTarget))
        {
            return ProjectPath.Normalize(effectiveTarget);
        }

        diagnostics.Add(
            new ManagedFileTemplateDiagnostic(declaredTarget, context.CurrentEffectiveTarget)
        );
        return declaredTarget;
    }

    private static string ResolveRelativePath(
        string declaredTarget,
        ManagedFileTemplateContext context,
        ICollection<ManagedFileTemplateDiagnostic> diagnostics
    )
    {
        if (!context.TryResolve(declaredTarget, out var effectiveTarget))
        {
            diagnostics.Add(
                new ManagedFileTemplateDiagnostic(declaredTarget, context.CurrentEffectiveTarget)
            );
            return declaredTarget;
        }

        var currentDirectory = Path.GetDirectoryName(context.CurrentEffectiveTarget);
        return ProjectPath.Normalize(
            Path.GetRelativePath(
                string.IsNullOrEmpty(currentDirectory) ? "." : currentDirectory,
                effectiveTarget
            )
        );
    }
}
