using System.IO.Abstractions;

namespace Lunapack.Cli;

internal sealed record PackInstallationRequest(
    PackReference PackReference,
    string? Destination,
    bool AdoptExisting
)
{
    public PackManagedFilePlanningMode PlanningMode { get; init; } =
        PackManagedFilePlanningMode.Install;

    public IReadOnlyDictionary<string, string> Parameters { get; init; } =
        new Dictionary<string, string>(StringComparer.Ordinal);

    public IReadOnlySet<string> SkippedVariables { get; init; } =
        new HashSet<string>(StringComparer.Ordinal);

    public ManagedFileTargetRemapping? TargetRemapping { get; init; }

    public bool UseProjectVariables { get; init; } = true;

    public bool AcceptSources { get; init; }

    public ScriptExecutionMode ScriptMode { get; init; } = ScriptExecutionMode.Prompt;

    public bool SkipInstructions { get; init; }

    public static ManifestOperationResult<PackInstallationRequest> Create(
        IFileSystem fileSystem,
        string projectDirectory,
        string packReferenceValue,
        string? destination,
        bool adoptExisting,
        IEnumerable<string> parameterValues,
        bool noVariables,
        IEnumerable<string> skippedVariables,
        IEnumerable<string>? directoryRemappings = null,
        IEnumerable<string>? fileRemappings = null,
        ScriptExecutionMode? scriptMode = null,
        bool skipInstructions = false
    )
    {
        var parsedPackReference = PackReference.Parse(packReferenceValue);
        if (parsedPackReference.Value is not { } packReference)
        {
            return ManifestOperationResult<PackInstallationRequest>.Failure(
                parsedPackReference.Error ?? "Invalid pack reference."
            );
        }

        var normalizedDestination = NormalizeDestination(destination);

        var inputs = ParseInputs(parameterValues, noVariables, skippedVariables);
        if (inputs.Value is not { } parsedInputs)
        {
            return ManifestOperationResult<PackInstallationRequest>.Failure(
                inputs.Error ?? "Invalid parameter input."
            );
        }

        var remapping = CreateTargetRemapping(
            fileSystem,
            projectDirectory,
            normalizedDestination,
            directoryRemappings ?? [],
            fileRemappings ?? []
        );
        if (remapping.Value is not { } parsedRemapping)
        {
            return ManifestOperationResult<PackInstallationRequest>.Failure(
                remapping.Error ?? "Invalid managed-file remapping."
            );
        }

        var destinationError = ValidateDestination(
            fileSystem,
            projectDirectory,
            normalizedDestination
        );
        return destinationError is not null
            ? ManifestOperationResult<PackInstallationRequest>.Failure(destinationError)
            : ManifestOperationResult<PackInstallationRequest>.Success(
                CreateRequest(
                    packReference,
                    normalizedDestination,
                    adoptExisting,
                    parsedInputs.Parameters,
                    parsedInputs.SkippedVariables,
                    parsedRemapping,
                    noVariables,
                    scriptMode ?? ScriptExecutionMode.Prompt,
                    skipInstructions
                )
            );
    }

    private static PackInstallationRequest CreateRequest(
        PackReference packReference,
        string? destination,
        bool adoptExisting,
        IReadOnlyDictionary<string, string> parameters,
        IReadOnlySet<string> skippedVariables,
        ManagedFileTargetRemapping targetRemapping,
        bool noVariables,
        ScriptExecutionMode scriptMode,
        bool skipInstructions
    ) =>
        new(packReference, destination, adoptExisting)
        {
            Parameters = parameters,
            SkippedVariables = skippedVariables,
            TargetRemapping = targetRemapping,
            UseProjectVariables = !noVariables,
            ScriptMode = scriptMode,
            SkipInstructions = skipInstructions,
        };

    private static string? NormalizeDestination(string? destination) =>
        ProjectPath.NormalizeOptional(destination);

    private static ManifestOperationResult<ParsedInputs> ParseInputs(
        IEnumerable<string> parameterValues,
        bool noVariables,
        IEnumerable<string> skippedVariables
    )
    {
        var parameters = ParseParameters(parameterValues);
        if (parameters.Value is not { } parsedParameters)
        {
            return ManifestOperationResult<ParsedInputs>.Failure(
                parameters.Error ?? "Invalid parameter input."
            );
        }

        var skips = ParseSkippedVariables(skippedVariables);
        if (skips.Value is not { } parsedSkips)
        {
            return ManifestOperationResult<ParsedInputs>.Failure(
                skips.Error ?? "Invalid skipped variable input."
            );
        }

        return noVariables && parsedSkips.Count > 0
            ? ManifestOperationResult<ParsedInputs>.Failure(
                "--no-variables cannot be combined with --skip-variable."
            )
            : ManifestOperationResult<ParsedInputs>.Success(
                new ParsedInputs(parsedParameters, parsedSkips)
            );
    }

    private static ManifestOperationResult<ManagedFileTargetRemapping> CreateTargetRemapping(
        IFileSystem fileSystem,
        string projectDirectory,
        string? destination,
        IEnumerable<string> directoryRemappings,
        IEnumerable<string> fileRemappings
    )
    {
        var remapping = ManagedFileTargetRemapping.Create(
            fileSystem,
            projectDirectory,
            directoryRemappings,
            fileRemappings
        );
        if (remapping.Value is not { } parsedRemapping)
        {
            return ManifestOperationResult<ManagedFileTargetRemapping>.Failure(
                remapping.Error ?? "Invalid managed-file remapping."
            );
        }

        return destination is not null && parsedRemapping.HasMappings
            ? ManifestOperationResult<ManagedFileTargetRemapping>.Failure(
                "--destination cannot be combined with --remap-directory or --remap-file."
            )
            : ManifestOperationResult<ManagedFileTargetRemapping>.Success(parsedRemapping);
    }

    private static ManifestOperationResult<IReadOnlyDictionary<string, string>> ParseParameters(
        IEnumerable<string> parameterValues
    )
    {
        var parameters = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var parameterValue in parameterValues)
        {
            var separatorIndex = parameterValue.IndexOf('=', StringComparison.Ordinal);
            if (separatorIndex <= 0 || !IsIdentifier(parameterValue[..separatorIndex]))
            {
                return ManifestOperationResult<IReadOnlyDictionary<string, string>>.Failure(
                    $"Invalid parameter '{parameterValue}'. Expected <name>=<value>."
                );
            }

            var name = parameterValue[..separatorIndex];
            if (!parameters.TryAdd(name, parameterValue[(separatorIndex + 1)..]))
            {
                return ManifestOperationResult<IReadOnlyDictionary<string, string>>.Failure(
                    $"Parameter '{name}' was supplied more than once."
                );
            }
        }

        return ManifestOperationResult<IReadOnlyDictionary<string, string>>.Success(parameters);
    }

    private static ManifestOperationResult<IReadOnlySet<string>> ParseSkippedVariables(
        IEnumerable<string> skippedVariables
    )
    {
        var skippedVariableNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var skippedVariable in skippedVariables)
        {
            if (!IsIdentifier(skippedVariable))
            {
                return ManifestOperationResult<IReadOnlySet<string>>.Failure(
                    $"Invalid skipped variable '{skippedVariable}'."
                );
            }

            skippedVariableNames.Add(skippedVariable);
        }

        return ManifestOperationResult<IReadOnlySet<string>>.Success(skippedVariableNames);
    }

    private static string? ValidateDestination(
        IFileSystem fileSystem,
        string projectDirectory,
        string? destination
    )
    {
        if (destination is null)
        {
            return null;
        }

        var path = ProjectPath.NormalizeProjectRelativePath(
            fileSystem,
            projectDirectory,
            destination
        );
        return path.Error?.Replace("Path", "Destination", StringComparison.Ordinal);
    }

    private static bool IsIdentifier(string value)
    {
        if (value.Length == 0 || !IsIdentifierStart(value[0]))
        {
            return false;
        }

        return value[1..].All(IsIdentifierPart);
    }

    private static bool IsIdentifierPart(char character) =>
        IsIdentifierStart(character) || character is >= '0' and <= '9';

    private static bool IsIdentifierStart(char character) =>
        character is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or '_';

    private sealed record ParsedInputs(
        IReadOnlyDictionary<string, string> Parameters,
        IReadOnlySet<string> SkippedVariables
    );
}
