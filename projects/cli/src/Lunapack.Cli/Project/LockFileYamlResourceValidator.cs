using Lunapack.Cli.Application.CommandExecution;
using Lunapack.Cli.Application.Preferences;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;

namespace Lunapack.Cli.Project;

internal static class LockFileYamlResourceValidator
{
    public static ManifestOperationResult<bool> Validate(
        string contents,
        ResourceLimits resourceLimits
    )
    {
        try
        {
            var parser = new Parser(new StringReader(contents));
            var contexts = new Stack<Context>();
            var counters = new Counters();
            while (parser.MoveNext())
            {
                if (parser.Current is not { } current)
                {
                    continue;
                }

                var processed = ProcessEvent(current, contexts, counters, resourceLimits);
                if (!processed.IsSuccess)
                {
                    return processed;
                }
            }

            return ManifestOperationResult<bool>.Success(true);
        }
        catch (YamlException)
        {
            return ManifestOperationResult<bool>.Success(true);
        }
        catch (YamlResourceLimitException exception)
        {
            return ManifestOperationResult<bool>.Failure(
                exception.Message,
                ManifestOperationErrorKind.ResourceLimitExceeded
            );
        }
    }

    private static ManifestOperationResult<bool> ProcessEvent(
        ParsingEvent current,
        Stack<Context> contexts,
        Counters counters,
        ResourceLimits resourceLimits
    ) =>
        current switch
        {
            MappingStart => BeginCollection(
                contexts,
                counters,
                resourceLimits,
                ContextKind.Mapping
            ),
            SequenceStart => BeginCollection(
                contexts,
                counters,
                resourceLimits,
                ContextKind.Sequence
            ),
            Scalar scalar => ProcessScalar(scalar, contexts, counters, resourceLimits),
            AnchorAlias => ManifestOperationResult<bool>.Failure(
                "Lock file YAML aliases are not allowed because they can expand retained collections after resource preflight."
            ),
            MappingEnd or SequenceEnd => contexts.TryPop(out _)
                ? ManifestOperationResult<bool>.Success(true)
                : ManifestOperationResult<bool>.Failure(
                    "Lock file YAML collection structure is invalid."
                ),
            _ => ManifestOperationResult<bool>.Success(true),
        };

    private static ManifestOperationResult<bool> BeginCollection(
        Stack<Context> contexts,
        Counters counters,
        ResourceLimits resourceLimits,
        ContextKind kind
    )
    {
        var path = BeginContainer(contexts, counters, resourceLimits);
        if (!path.IsSuccess)
        {
            return ManifestOperationResult<bool>.Failure(path.Error!, path.ErrorKind);
        }

        contexts.Push(new Context(kind, path.Value!));
        return ManifestOperationResult<bool>.Success(true);
    }

    private static ManifestOperationResult<bool> ProcessScalar(
        Scalar scalar,
        Stack<Context> contexts,
        Counters counters,
        ResourceLimits resourceLimits
    )
    {
        if (
            contexts.TryPeek(out var context)
            && context.Kind == ContextKind.Mapping
            && context.ExpectingKey
        )
        {
            context.Key = scalar.Value ?? string.Empty;
            context.ExpectingKey = false;
            return ManifestOperationResult<bool>.Success(true);
        }

        var consumed = ConsumeValue(contexts, counters, resourceLimits);
        return consumed.IsSuccess
            ? ManifestOperationResult<bool>.Success(true)
            : ManifestOperationResult<bool>.Failure(consumed.Error!, consumed.ErrorKind);
    }

    private static ManifestOperationResult<string> BeginContainer(
        Stack<Context> contexts,
        Counters counters,
        ResourceLimits resourceLimits
    )
    {
        if (contexts.Count == 0)
        {
            return ManifestOperationResult<string>.Success(string.Empty);
        }

        return ConsumeValue(contexts, counters, resourceLimits);
    }

    private static ManifestOperationResult<string> ConsumeValue(
        Stack<Context> contexts,
        Counters counters,
        ResourceLimits resourceLimits
    )
    {
        if (contexts.Count == 0)
        {
            return ManifestOperationResult<string>.Success(string.Empty);
        }

        var parent = contexts.Peek();
        return parent.Kind switch
        {
            ContextKind.Mapping when parent.ExpectingKey => ManifestOperationResult<string>.Failure(
                "Lock file YAML complex mapping keys are not allowed."
            ),
            ContextKind.Mapping => ManifestOperationResult<string>.Success(
                RegisterMappingValue(parent, resourceLimits)
            ),
            ContextKind.Sequence => ManifestOperationResult<string>.Success(
                RegisterSequenceValue(parent, counters, resourceLimits) + "[]"
            ),
            _ => ManifestOperationResult<string>.Failure(
                "Lock file YAML collection structure is invalid."
            ),
        };
    }

    private static string RegisterMappingValue(Context context, ResourceLimits resourceLimits)
    {
        var key = context.Key;
        context.Key = null;
        context.ExpectingKey = true;
        var countsDynamicEntries =
            string.Equals(context.Path, "links", StringComparison.Ordinal)
            || context.Path.EndsWith(".externalSources", StringComparison.Ordinal);
        if (countsDynamicEntries)
        {
            context.ItemCount = checked(context.ItemCount + 1);
            if (context.ItemCount > resourceLimits.FilesPerGraph)
            {
                throw new YamlResourceLimitException(
                    $"Lock file mapping '{context.Path}' contains at least {context.ItemCount} entries, exceeding its {resourceLimits.FilesPerGraph} record limit."
                );
            }
        }

        if (string.Equals(context.Path, "links", StringComparison.Ordinal))
        {
            return "links[]";
        }

        return string.IsNullOrEmpty(context.Path) ? key! : $"{context.Path}.{key}";
    }

    private static string RegisterSequenceValue(
        Context context,
        Counters counters,
        ResourceLimits resourceLimits
    )
    {
        context.ItemCount = checked(context.ItemCount + 1);
        if (context.ItemCount > resourceLimits.FilesPerGraph)
        {
            throw new YamlResourceLimitException(
                $"Lock file sequence '{context.Path}' contains at least {context.ItemCount} entries, exceeding its {resourceLimits.FilesPerGraph} record limit."
            );
        }

        switch (context.Path)
        {
            case "packs":
                if (context.ItemCount > resourceLimits.DependencyNodes)
                {
                    throw new YamlResourceLimitException(
                        $"Lock file contains at least {context.ItemCount} pack records, exceeding its {resourceLimits.DependencyNodes} node limit."
                    );
                }

                break;
            case "packs[].packs":
                counters.ReferenceCount++;
                if (counters.ReferenceCount > resourceLimits.DependencyReferences)
                {
                    throw new YamlResourceLimitException(
                        $"Lock file contains at least {counters.ReferenceCount} pack references, exceeding its {resourceLimits.DependencyReferences} reference limit."
                    );
                }

                break;
            case "packs[].managedFiles":
                RegisterFile(context.ItemCount, counters, resourceLimits);
                break;
            case "links[].files":
                RegisterFile(context.ItemCount, counters, resourceLimits);
                break;
        }

        return context.Path;
    }

    private static void RegisterFile(
        long perOwnerCount,
        Counters counters,
        ResourceLimits resourceLimits
    )
    {
        if (perOwnerCount > resourceLimits.FilesPerPack)
        {
            throw new YamlResourceLimitException(
                $"Lock file record contains at least {perOwnerCount} files, exceeding its {resourceLimits.FilesPerPack} file limit."
            );
        }

        counters.GraphFileCount++;
        if (counters.GraphFileCount > resourceLimits.FilesPerGraph)
        {
            throw new YamlResourceLimitException(
                $"Lock file contains at least {counters.GraphFileCount} managed and linked files, exceeding its {resourceLimits.FilesPerGraph} graph file limit."
            );
        }
    }

    private sealed class Context(ContextKind kind, string path)
    {
        public bool ExpectingKey { get; set; } = kind == ContextKind.Mapping;

        public long ItemCount { get; set; }

        public string? Key { get; set; }

        public ContextKind Kind { get; } = kind;

        public string Path { get; } = path;
    }

    private sealed class Counters
    {
        public long GraphFileCount { get; set; }

        public long ReferenceCount { get; set; }
    }

    private enum ContextKind
    {
        Mapping,
        Sequence,
    }

    private sealed class YamlResourceLimitException(string message) : Exception(message);
}
