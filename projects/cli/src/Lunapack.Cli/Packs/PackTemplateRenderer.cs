using System.IO.Abstractions;
using System.Text;
using Lunapack.Cli.Application.CommandExecution;
using Lunapack.Cli.Application.Paths;
using Lunapack.Cli.Application.Preferences;
using Lunapack.Cli.Packs.ManagedFiles;
using Scriban;
using Scriban.Runtime;
using Scriban.Syntax;

namespace Lunapack.Cli.Packs;

internal sealed class PackTemplateRenderer(IFileSystem fileSystem)
{
    private static readonly UTF8Encoding _utf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true
    );

    public ManifestOperationResult<byte[]> Render(
        string templatePath,
        bool isTemplate,
        ResolvedPackParameters parameters,
        ResourceLimits? resourceLimits = null,
        RenderedOutputBudget? outputBudget = null
    )
    {
        try
        {
            var limits = resourceLimits ?? ResourceLimits.Default;
            var contents = BoundedFileReader.ReadBytes(
                fileSystem,
                templatePath,
                limits.PackDocumentBytes,
                $"Pack document '{templatePath}'"
            );
            if (contents.Value is not { } bytes)
            {
                return ManifestOperationResult<byte[]>.Failure(contents.Error!, contents.ErrorKind);
            }

            if (!isTemplate)
            {
                var observed = (outputBudget ?? new RenderedOutputBudget(limits)).Observe(
                    templatePath,
                    bytes.Length,
                    bytes.Length
                );
                return observed.IsSuccess
                    ? ManifestOperationResult<byte[]>.Success(bytes)
                    : ManifestOperationResult<byte[]>.Failure(observed.Error!, observed.ErrorKind);
            }

            var templateText = _utf8.GetString(bytes);
            return RenderBytes(
                templateText,
                templatePath,
                CreateContext(parameters, null, null),
                outputBudget ?? new RenderedOutputBudget(limits)
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
        ManagedFileTemplateContext managedFileContext,
        ResourceLimits? resourceLimits = null,
        RenderedOutputBudget? outputBudget = null
    )
    {
        try
        {
            var limits = resourceLimits ?? ResourceLimits.Default;
            var contents = BoundedFileReader.ReadBytes(
                fileSystem,
                templatePath,
                isTemplate ? limits.PackDocumentBytes : limits.RenderedFileBytes,
                $"Managed file '{templatePath}'"
            );
            if (contents.Value is not { } bytes)
            {
                return ManifestOperationResult<RenderedManagedFileTemplate>.Failure(
                    contents.Error!,
                    contents.ErrorKind
                );
            }

            if (!isTemplate)
            {
                var observed = (outputBudget ?? new RenderedOutputBudget(limits)).Observe(
                    templatePath,
                    bytes.Length,
                    bytes.Length
                );
                return observed.IsSuccess
                    ? ManifestOperationResult<RenderedManagedFileTemplate>.Success(
                        new RenderedManagedFileTemplate(bytes, [])
                    )
                    : ManifestOperationResult<RenderedManagedFileTemplate>.Failure(
                        observed.Error!,
                        observed.ErrorKind
                    );
            }

            var templateText = _utf8.GetString(bytes);
            var diagnostics = new List<ManagedFileTemplateDiagnostic>();
            var rendered = RenderBytes(
                templateText,
                templatePath,
                CreateContext(parameters, managedFileContext, diagnostics),
                outputBudget ?? new RenderedOutputBudget(limits)
            );
            return rendered.Value is { } value
                ? ManifestOperationResult<RenderedManagedFileTemplate>.Success(
                    new RenderedManagedFileTemplate(value, diagnostics)
                )
                : ManifestOperationResult<RenderedManagedFileTemplate>.Failure(
                    rendered.Error ?? $"Template '{templatePath}' cannot be rendered.",
                    rendered.ErrorKind
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
        ResolvedPackParameters parameters,
        ResourceLimits? resourceLimits = null,
        RenderedOutputBudget? outputBudget = null
    )
    {
        var limits = resourceLimits ?? ResourceLimits.Default;
        var rendered = RenderBytes(
            templateText,
            templateName,
            CreateContext(parameters, null, null),
            outputBudget ?? new RenderedOutputBudget(limits)
        );
        return rendered.Value is { } bytes
            ? ManifestOperationResult<string>.Success(_utf8.GetString(bytes))
            : ManifestOperationResult<string>.Failure(rendered.Error!, rendered.ErrorKind);
    }

    private static ManifestOperationResult<byte[]> RenderBytes(
        string templateText,
        string templateName,
        TemplateContext context,
        RenderedOutputBudget outputBudget
    )
    {
        try
        {
            var template = Template.Parse(templateText, templateName);
            if (template.HasErrors)
            {
                return ManifestOperationResult<byte[]>.Failure(
                    $"Template '{templateName}' cannot be parsed: {string.Join(Environment.NewLine, template.Messages)}"
                );
            }

            using var output = new BoundedScriptOutput(templateName, outputBudget);
            context.PushOutput(output);
            try
            {
                template.Render(context);
                return ManifestOperationResult<byte[]>.Success(output.ToArray());
            }
            finally
            {
                context.PopOutput();
            }
        }
        catch (RenderedOutputLimitException exception)
        {
            return ManifestOperationResult<byte[]>.Failure(
                exception.Message,
                ManifestOperationErrorKind.ResourceLimitExceeded
            );
        }
        catch (Exception exception)
            when (exception
                    is ScriptRuntimeException
                        or EncoderFallbackException
                        or OverflowException
            )
        {
            return ManifestOperationResult<byte[]>.Failure(
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

        var requiresContainsCompatibilityFlag =
            parameters.Values.Values.Any(value => value.StringValues is not null)
            && !parameters.Values.ContainsKey("contains");
        if (requiresContainsCompatibilityFlag)
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

    private sealed class BoundedScriptOutput(string outputName, RenderedOutputBudget outputBudget)
        : IScriptOutput,
            IDisposable
    {
        private readonly MemoryStream _contents = new();
        private long _fileBytes;

        public void Write(string text, int offset, int count)
        {
            ArgumentNullException.ThrowIfNull(text);

            if ((uint)offset > (uint)text.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(offset));
            }

            if (count < 0 || count > text.Length - offset)
            {
                throw new ArgumentOutOfRangeException(nameof(count));
            }

            if (count == 0)
            {
                return;
            }

            var byteCount = _utf8.GetByteCount(text.AsSpan(offset, count));
            var observedFileBytes = checked(_fileBytes + byteCount);
            var observed = outputBudget.Observe(outputName, byteCount, observedFileBytes);
            if (!observed.IsSuccess)
            {
                throw new RenderedOutputLimitException(
                    observed.Error ?? "Rendered output exceeds its configured limit."
                );
            }

            _fileBytes = observedFileBytes;
            _contents.Write(_utf8.GetBytes(text.Substring(offset, count)));
        }

        public ValueTask WriteAsync(
            string text,
            int offset,
            int count,
            CancellationToken cancellationToken
        )
        {
            cancellationToken.ThrowIfCancellationRequested();
            Write(text, offset, count);
            return ValueTask.CompletedTask;
        }

        public byte[] ToArray() => _contents.ToArray();

        public void Dispose() => _contents.Dispose();
    }

    private sealed class RenderedOutputLimitException(string message) : Exception(message);

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
