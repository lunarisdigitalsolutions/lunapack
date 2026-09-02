using Lunapack.Cli.Application.CommandExecution;
using Lunapack.Cli.Catalog;
using Lunapack.Cli.Packs.Manifest;
using Lunapack.Cli.Packs.Planning;
using Lunapack.Cli.Project;

namespace Lunapack.Cli.Packs;

internal static class PackParameterResolver
{
    public static ManifestOperationResult<
        IReadOnlyDictionary<string, IReadOnlyList<string>>
    > Prompt(
        ResolvedPackGraph graph,
        ProjectConfiguration configuration,
        PackInstallationRequest installationRequest,
        bool includeOptional,
        PackParameterPromptCallback promptParameters
    )
    {
        var values = installationRequest
            .GetParameterValues()
            .ToDictionary(
                parameter => parameter.Key,
                parameter => parameter.Value,
                StringComparer.Ordinal
            );
        while (true)
        {
            var request = installationRequest with { ParameterValues = values };
            var next = FindNextPromptable(graph, configuration, request, includeOptional);
            if (next.Value is not { } prompts)
            {
                return ManifestOperationResult<
                    IReadOnlyDictionary<string, IReadOnlyList<string>>
                >.Failure(next.Error ?? "Unable to resolve the next pack parameter prompt.");
            }

            if (prompts.Count == 0)
            {
                return ManifestOperationResult<
                    IReadOnlyDictionary<string, IReadOnlyList<string>>
                >.Success(values);
            }

            var prompt = prompts[0];
            var prompted = promptParameters(prompts);
            if (!prompted.TryGetValue(prompt.Id, out var promptedValue))
            {
                return ManifestOperationResult<
                    IReadOnlyDictionary<string, IReadOnlyList<string>>
                >.Failure($"Parameter prompt did not return a value for '{prompt.Id}'.");
            }

            values[prompt.Id] = promptedValue;
        }
    }

    public static ManifestOperationResult<ResolvedPackParameters> Resolve(
        ResolvedPackGraph graph,
        ProjectConfiguration configuration,
        PackInstallationRequest installationRequest
    )
    {
        var declarations = CollectDeclarations(graph);
        if (declarations.Value is not { } resolvedDeclarations)
        {
            return ManifestOperationResult<ResolvedPackParameters>.Failure(
                declarations.Error ?? "Unable to resolve pack parameter declarations."
            );
        }

        var compositeValues = CollectCompositeValues(graph, resolvedDeclarations);
        if (compositeValues.Value is not { } resolvedCompositeValues)
        {
            return ManifestOperationResult<ResolvedPackParameters>.Failure(
                compositeValues.Error ?? "Unable to resolve composite pack parameters."
            );
        }

        return BindValues(
            resolvedDeclarations,
            resolvedCompositeValues,
            configuration,
            installationRequest,
            enforceRequired: true
        );
    }

    public static ManifestOperationResult<ResolvedPackParameters> ResolveForSelection(
        ResolvedPackGraph graph,
        ProjectConfiguration configuration,
        PackInstallationRequest installationRequest
    )
    {
        var declarations = CollectDeclarations(graph);
        return declarations.Value is { } resolvedDeclarations
            ? BindValues(
                resolvedDeclarations,
                new Dictionary<string, object>(StringComparer.Ordinal),
                configuration,
                installationRequest,
                enforceRequired: false
            )
            : ManifestOperationResult<ResolvedPackParameters>.Failure(
                declarations.Error ?? "Unable to resolve pack parameter declarations."
            );
    }

    public static ManifestOperationResult<
        IReadOnlyList<PackParameterPrompt>
    > FindUnresolvedRequired(
        ResolvedPackGraph graph,
        ProjectConfiguration configuration,
        PackInstallationRequest installationRequest
    ) => FindPromptable(graph, configuration, installationRequest, includeOptional: false);

    public static ManifestOperationResult<IReadOnlyList<PackParameterPrompt>> FindPromptable(
        ResolvedPackGraph graph,
        ProjectConfiguration configuration,
        PackInstallationRequest installationRequest,
        bool includeOptional
    )
    {
        var declarations = CollectDeclarations(graph);
        if (declarations.Value is not { } resolvedDeclarations)
        {
            return ManifestOperationResult<IReadOnlyList<PackParameterPrompt>>.Failure(
                declarations.Error ?? "Unable to resolve pack parameter declarations."
            );
        }

        var compositeValues = CollectCompositeValues(graph, resolvedDeclarations);
        if (compositeValues.Value is not { } resolvedCompositeValues)
        {
            return ManifestOperationResult<IReadOnlyList<PackParameterPrompt>>.Failure(
                compositeValues.Error ?? "Unable to resolve composite pack parameters."
            );
        }

        var inputValues = installationRequest.GetParameterValues();
        var partialParameters = BindValues(
            resolvedDeclarations,
            resolvedCompositeValues,
            configuration,
            installationRequest,
            enforceRequired: false
        );
        if (partialParameters.Value is not { } parameters)
        {
            return ManifestOperationResult<IReadOnlyList<PackParameterPrompt>>.Failure(
                partialParameters.Error ?? "Unable to resolve pack parameters."
            );
        }

        var unresolved = new List<PackParameterPrompt>();
        foreach (var (name, declaration) in resolvedDeclarations)
        {
            var required = IsRequired(declaration, resolvedDeclarations, parameters.Values);
            if (!required.IsSuccess)
            {
                return ManifestOperationResult<IReadOnlyList<PackParameterPrompt>>.Failure(
                    required.Error ?? $"Unable to evaluate requiredWhen for parameter '{name}'."
                );
            }

            var usesProjectVariable =
                !includeOptional
                && installationRequest.UseProjectVariables
                && !installationRequest.SkippedVariables.Contains(name)
                && configuration.Variables.ContainsKey(name);
            if (
                (includeOptional || required.Value)
                && !resolvedCompositeValues.ContainsKey(name)
                && !inputValues.ContainsKey(name)
                && !usesProjectVariable
            )
            {
                unresolved.Add(new PackParameterPrompt(name, declaration));
            }
        }

        return ManifestOperationResult<IReadOnlyList<PackParameterPrompt>>.Success(unresolved);
    }

    private static ManifestOperationResult<IReadOnlyList<PackParameterPrompt>> FindNextPromptable(
        ResolvedPackGraph graph,
        ProjectConfiguration configuration,
        PackInstallationRequest installationRequest,
        bool includeOptional
    )
    {
        var declarations = CollectDeclarations(graph);
        if (declarations.Value is not { } resolvedDeclarations)
        {
            return ManifestOperationResult<IReadOnlyList<PackParameterPrompt>>.Failure(
                declarations.Error ?? "Unable to resolve pack parameter declarations."
            );
        }

        var selectionParameters = ResolveForSelection(graph, configuration, installationRequest);
        if (selectionParameters.Value is not { } parametersForSelection)
        {
            return ManifestOperationResult<IReadOnlyList<PackParameterPrompt>>.Failure(
                selectionParameters.Error ?? "Unable to resolve parameters for graph selection."
            );
        }

        var selection = graph.Select(parametersForSelection);
        if (selection.Value is not { } selectedGraph)
        {
            return ManifestOperationResult<IReadOnlyList<PackParameterPrompt>>.Failure(
                selection.Error ?? "Unable to select conditional pack references."
            );
        }

        var compositeValues = CollectCompositeValues(selectedGraph, resolvedDeclarations);
        if (compositeValues.Value is not { } resolvedCompositeValues)
        {
            return ManifestOperationResult<IReadOnlyList<PackParameterPrompt>>.Failure(
                compositeValues.Error ?? "Unable to resolve composite pack parameters."
            );
        }

        var partialParameters = BindValues(
            resolvedDeclarations,
            resolvedCompositeValues,
            configuration,
            installationRequest,
            enforceRequired: false
        );
        if (partialParameters.Value is not { } parameters)
        {
            return ManifestOperationResult<IReadOnlyList<PackParameterPrompt>>.Failure(
                partialParameters.Error ?? "Unable to resolve pack parameters."
            );
        }

        var packsById = selectedGraph.Packs.ToDictionary(
            pack => pack.Manifest.Id,
            StringComparer.Ordinal
        );
        var visited = new HashSet<string>(StringComparer.Ordinal);
        foreach (var root in selectedGraph.Packs.Where(selectedGraph.IsRoot))
        {
            var next = FindNextInPack(
                root,
                packsById,
                resolvedDeclarations,
                resolvedCompositeValues,
                parameters,
                configuration,
                installationRequest,
                includeOptional,
                visited,
                selectedGraph.ActiveReferences
            );
            if (!next.IsSuccess || next.Value is { Count: > 0 })
            {
                return next;
            }
        }

        return ManifestOperationResult<IReadOnlyList<PackParameterPrompt>>.Success([]);
    }

    private static ManifestOperationResult<IReadOnlyList<PackParameterPrompt>> FindNextInPack(
        DiscoveredPack pack,
        IReadOnlyDictionary<string, DiscoveredPack> packsById,
        IReadOnlyDictionary<string, PackParameterDefinition> declarations,
        IReadOnlyDictionary<string, object> compositeValues,
        ResolvedPackParameters parameters,
        ProjectConfiguration configuration,
        PackInstallationRequest installationRequest,
        bool includeOptional,
        ISet<string> visited,
        IReadOnlySet<PackManifest.PackReference>? activeReferences
    )
    {
        if (!visited.Add(pack.Manifest.Id))
        {
            return ManifestOperationResult<IReadOnlyList<PackParameterPrompt>>.Success([]);
        }

        foreach (var name in pack.Manifest.Parameters.Keys)
        {
            var prompt = CreatePrompt(
                name,
                declarations,
                compositeValues,
                parameters,
                configuration,
                installationRequest,
                includeOptional
            );
            if (!prompt.IsSuccess || prompt.Value is { Count: > 0 })
            {
                return prompt;
            }
        }

        foreach (var reference in pack.Manifest.Packs)
        {
            if (
                (activeReferences is null || activeReferences.Contains(reference))
                && packsById.TryGetValue(reference.Id, out var dependency)
            )
            {
                var next = FindNextInPack(
                    dependency,
                    packsById,
                    declarations,
                    compositeValues,
                    parameters,
                    configuration,
                    installationRequest,
                    includeOptional,
                    visited,
                    activeReferences
                );
                if (!next.IsSuccess || next.Value is { Count: > 0 })
                {
                    return next;
                }
            }
        }

        return ManifestOperationResult<IReadOnlyList<PackParameterPrompt>>.Success([]);
    }

    private static ManifestOperationResult<IReadOnlyList<PackParameterPrompt>> CreatePrompt(
        string name,
        IReadOnlyDictionary<string, PackParameterDefinition> declarations,
        IReadOnlyDictionary<string, object> compositeValues,
        ResolvedPackParameters parameters,
        ProjectConfiguration configuration,
        PackInstallationRequest installationRequest,
        bool includeOptional
    )
    {
        if (
            compositeValues.ContainsKey(name)
            || installationRequest.GetParameterValues().ContainsKey(name)
        )
        {
            return ManifestOperationResult<IReadOnlyList<PackParameterPrompt>>.Success([]);
        }

        var usesProjectVariable =
            !includeOptional
            && installationRequest.UseProjectVariables
            && !installationRequest.SkippedVariables.Contains(name)
            && configuration.Variables.ContainsKey(name);
        if (usesProjectVariable)
        {
            return ManifestOperationResult<IReadOnlyList<PackParameterPrompt>>.Success([]);
        }

        var declaration = declarations[name];
        var required = IsRequired(declaration, declarations, parameters.Values);
        if (!required.IsSuccess)
        {
            return ManifestOperationResult<IReadOnlyList<PackParameterPrompt>>.Failure(
                required.Error ?? $"Unable to evaluate requiredWhen for parameter '{name}'."
            );
        }

        return ManifestOperationResult<IReadOnlyList<PackParameterPrompt>>.Success(
            includeOptional || required.Value ? [new PackParameterPrompt(name, declaration)] : []
        );
    }

    private static ManifestOperationResult<
        IReadOnlyDictionary<string, PackParameterDefinition>
    > CollectDeclarations(ResolvedPackGraph graph)
    {
        var declarations = new Dictionary<string, PackParameterDefinition>(StringComparer.Ordinal);
        var declaringPackIds = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var pack in graph.Packs.Reverse())
        {
            foreach (var (name, declaration) in pack.Manifest.Parameters)
            {
                var parsedDeclaration = ParseDeclaration(name, declaration, pack.Manifest.Id);
                if (parsedDeclaration.Value is not { } parameterDeclaration)
                {
                    return ManifestOperationResult<
                        IReadOnlyDictionary<string, PackParameterDefinition>
                    >.Failure(parsedDeclaration.Error ?? "Invalid pack parameter declaration.");
                }

                if (declarations.TryGetValue(name, out var existingDeclaration))
                {
                    var declarationsAreIncompatible =
                        existingDeclaration.Type != parameterDeclaration.Type
                        || existingDeclaration.Multiple != parameterDeclaration.Multiple;
                    if (declarationsAreIncompatible)
                    {
                        return ManifestOperationResult<
                            IReadOnlyDictionary<string, PackParameterDefinition>
                        >.Failure(
                            $"Parameter '{name}' is declared incompatibly by packs '{declaringPackIds[name]}' and '{pack.Manifest.Id}'."
                        );
                    }

                    continue;
                }

                declarations.Add(name, parameterDeclaration);
                declaringPackIds.Add(name, pack.Manifest.Id);
            }
        }

        return ManifestOperationResult<
            IReadOnlyDictionary<string, PackParameterDefinition>
        >.Success(declarations);
    }

    private static ManifestOperationResult<
        IReadOnlyDictionary<string, object>
    > CollectCompositeValues(
        ResolvedPackGraph graph,
        IReadOnlyDictionary<string, PackParameterDefinition> declarations
    )
    {
        var rootParameters = graph
            .Packs.Where(graph.IsRoot)
            .SelectMany(pack => pack.Manifest.Parameters.Keys)
            .ToHashSet(StringComparer.Ordinal);
        var compositeValues = new Dictionary<string, object>(StringComparer.Ordinal);
        foreach (var pack in graph.Packs.Reverse())
        {
            foreach (var reference in pack.Manifest.Packs)
            {
                if (
                    graph.ActiveReferences is not null
                    && !graph.ActiveReferences.Contains(reference)
                )
                {
                    continue;
                }

                foreach (var (name, value) in reference.Parameters)
                {
                    if (!declarations.ContainsKey(name))
                    {
                        return ManifestOperationResult<IReadOnlyDictionary<string, object>>.Failure(
                            $"Composite pack '{pack.Manifest.Id}' sets undeclared parameter '{name}'."
                        );
                    }

                    if (!rootParameters.Contains(name))
                    {
                        compositeValues.TryAdd(name, value);
                    }
                }
            }
        }

        return ManifestOperationResult<IReadOnlyDictionary<string, object>>.Success(
            compositeValues
        );
    }

    private static ManifestOperationResult<ResolvedPackParameters> BindValues(
        IReadOnlyDictionary<string, PackParameterDefinition> declarations,
        IReadOnlyDictionary<string, object> compositeValues,
        ProjectConfiguration configuration,
        PackInstallationRequest installationRequest,
        bool enforceRequired
    )
    {
        var skippedVariableError = ValidateSkippedVariables(
            declarations,
            compositeValues,
            installationRequest
        );
        if (skippedVariableError is not null)
        {
            return ManifestOperationResult<ResolvedPackParameters>.Failure(skippedVariableError);
        }

        var resolvedValues = new Dictionary<string, ResolvedPackParameterValue>(
            StringComparer.Ordinal
        );
        var compositeValueError = AddCompositeValues(declarations, compositeValues, resolvedValues);
        if (compositeValueError is not null)
        {
            return ManifestOperationResult<ResolvedPackParameters>.Failure(compositeValueError);
        }

        var providedValueError = AddProvidedParameterValues(
            declarations,
            compositeValues,
            installationRequest,
            resolvedValues
        );
        if (providedValueError is not null)
        {
            return ManifestOperationResult<ResolvedPackParameters>.Failure(providedValueError);
        }

        var fallbackValueError = AddProjectVariableOrDefaultValues(
            declarations,
            configuration,
            installationRequest,
            resolvedValues,
            enforceRequired
        );
        if (fallbackValueError is not null)
        {
            return ManifestOperationResult<ResolvedPackParameters>.Failure(fallbackValueError);
        }

        return ManifestOperationResult<ResolvedPackParameters>.Success(
            new ResolvedPackParameters(declarations, resolvedValues)
        );
    }

    private static string? ValidateSkippedVariables(
        IReadOnlyDictionary<string, PackParameterDefinition> declarations,
        IReadOnlyDictionary<string, object> compositeValues,
        PackInstallationRequest installationRequest
    )
    {
        foreach (var skippedVariable in installationRequest.SkippedVariables)
        {
            if (compositeValues.ContainsKey(skippedVariable))
            {
                return $"Parameter '{skippedVariable}' is fixed by a composite pack.";
            }

            if (!declarations.ContainsKey(skippedVariable))
            {
                return $"Project variable '{skippedVariable}' is not declared by the resolved pack graph.";
            }
        }

        return null;
    }

    private static string? AddCompositeValues(
        IReadOnlyDictionary<string, PackParameterDefinition> declarations,
        IReadOnlyDictionary<string, object> compositeValues,
        Dictionary<string, ResolvedPackParameterValue> resolvedValues
    )
    {
        foreach (var (name, value) in compositeValues)
        {
            var compositeValue = ParseCompositeValue(name, declarations[name], value);
            if (compositeValue.Value is not { } resolvedValue)
            {
                return compositeValue.Error ?? $"Invalid composite value for parameter '{name}'.";
            }

            resolvedValues.Add(name, resolvedValue);
        }

        return null;
    }

    private static string? AddProvidedParameterValues(
        IReadOnlyDictionary<string, PackParameterDefinition> declarations,
        IReadOnlyDictionary<string, object> compositeValues,
        PackInstallationRequest installationRequest,
        Dictionary<string, ResolvedPackParameterValue> resolvedValues
    )
    {
        foreach (var (name, values) in installationRequest.GetParameterValues())
        {
            if (!declarations.TryGetValue(name, out var declaration))
            {
                return $"Parameter '{name}' is not declared by the resolved pack graph.";
            }

            if (compositeValues.ContainsKey(name))
            {
                return $"Parameter '{name}' is fixed by a composite pack.";
            }

            var commandLineValue = ParseCommandLineValue(name, declaration, values);
            if (commandLineValue.Value is not { } resolvedValue)
            {
                return commandLineValue.Error ?? $"Invalid value for parameter '{name}'.";
            }

            resolvedValues.Add(name, resolvedValue);
        }

        return null;
    }

    private static string? AddProjectVariableOrDefaultValues(
        IReadOnlyDictionary<string, PackParameterDefinition> declarations,
        ProjectConfiguration configuration,
        PackInstallationRequest installationRequest,
        Dictionary<string, ResolvedPackParameterValue> resolvedValues,
        bool enforceRequired
    )
    {
        foreach (var (name, declaration) in declarations)
        {
            if (resolvedValues.ContainsKey(name))
            {
                continue;
            }

            if (
                installationRequest.UseProjectVariables
                && !installationRequest.SkippedVariables.Contains(name)
                && configuration.Variables.TryGetValue(name, out var variable)
            )
            {
                var projectValue = ParseProjectVariableValue(name, declaration, variable);
                if (projectValue.Value is not { } resolvedValue)
                {
                    return projectValue.Error ?? $"Invalid project variable '{name}'.";
                }

                resolvedValues.Add(name, resolvedValue);
                continue;
            }

            var required = IsRequired(declaration, declarations, resolvedValues);
            if (!required.IsSuccess)
            {
                return required.Error;
            }

            if (enforceRequired && required.Value)
            {
                return $"Required parameter '{name}' has no resolved value.";
            }

            var defaultValue = ParseDefaultValue(name, declaration);
            if (defaultValue.Value is not { } resolvedDefault)
            {
                return defaultValue.Error ?? $"Invalid default value for parameter '{name}'.";
            }

            resolvedValues.Add(name, resolvedDefault);
        }

        return null;
    }

    private static ManifestOperationResult<PackParameterDefinition> ParseDeclaration(
        string name,
        PackManifest.PackParameter declaration,
        string packId
    )
    {
        var type = declaration.Type switch
        {
            "string" => PackParameterType.String,
            "bool" => PackParameterType.Bool,
            "enum" => PackParameterType.Enum,
            _ => (PackParameterType?)null,
        };
        if (type is null)
        {
            return ManifestOperationResult<PackParameterDefinition>.Failure(
                $"Parameter '{name}' in pack '{packId}' has an unsupported type."
            );
        }

        var values = declaration.Values ?? [];
        if (type == PackParameterType.Enum)
        {
            var distinctValues = new HashSet<string>(values, StringComparer.Ordinal);
            if (values.Count == 0 || distinctValues.Count != values.Count)
            {
                return ManifestOperationResult<PackParameterDefinition>.Failure(
                    $"Enum parameter '{name}' in pack '{packId}' must declare distinct values."
                );
            }

            return ManifestOperationResult<PackParameterDefinition>.Success(
                new PackParameterDefinition(
                    type.Value,
                    declaration.Required is true,
                    values,
                    declaration.DisplayName,
                    declaration.Description,
                    declaration.Default,
                    declaration.Multiple is true,
                    declaration.RequiredWhen
                )
            );
        }

        return values.Count == 0
            ? ManifestOperationResult<PackParameterDefinition>.Success(
                new PackParameterDefinition(
                    type.Value,
                    declaration.Required is true,
                    [],
                    declaration.DisplayName,
                    declaration.Description,
                    declaration.Default,
                    false,
                    declaration.RequiredWhen
                )
            )
            : ManifestOperationResult<PackParameterDefinition>.Failure(
                $"Parameter '{name}' in pack '{packId}' may only declare values when its type is enum."
            );
    }

    private static ManifestOperationResult<bool> IsRequired(
        PackParameterDefinition declaration,
        IReadOnlyDictionary<string, PackParameterDefinition> declarations,
        IReadOnlyDictionary<string, ResolvedPackParameterValue> values
    )
    {
        if (declaration.Required)
        {
            return ManifestOperationResult<bool>.Success(true);
        }

        if (declaration.RequiredWhen is not { } requiredWhen)
        {
            return ManifestOperationResult<bool>.Success(false);
        }

        var parsed = ManagedFileConditionParser.Parse(requiredWhen, declarations);
        return parsed.Value is { } condition
            ? ManifestOperationResult<bool>.Success(condition.Evaluate(values))
            : ManifestOperationResult<bool>.Failure(
                parsed.Error ?? "Unable to evaluate parameter requiredWhen."
            );
    }

    private static ManifestOperationResult<ResolvedPackParameterValue> ParseCommandLineValue(
        string name,
        PackParameterDefinition declaration,
        IReadOnlyList<string> values
    )
    {
        if (!declaration.Multiple && values.Count != 1)
        {
            return ManifestOperationResult<ResolvedPackParameterValue>.Failure(
                $"Parameter '{name}' was supplied more than once."
            );
        }

        if (declaration.Multiple)
        {
            return CreateStringValues(name, declaration, values, "Parameter");
        }

        var value = values[0];
        if (declaration.Type == PackParameterType.Bool)
        {
            return value switch
            {
                "true" => ManifestOperationResult<ResolvedPackParameterValue>.Success(
                    new ResolvedPackParameterValue(declaration.Type, string.Empty, true)
                ),
                "false" => ManifestOperationResult<ResolvedPackParameterValue>.Success(
                    new ResolvedPackParameterValue(declaration.Type, string.Empty, false)
                ),
                _ => ManifestOperationResult<ResolvedPackParameterValue>.Failure(
                    $"Boolean parameter '{name}' must be 'true' or 'false'."
                ),
            };
        }

        return CreateStringValue(name, declaration, value, "Parameter");
    }

    private static ManifestOperationResult<ResolvedPackParameterValue> ParseDefaultValue(
        string name,
        PackParameterDefinition declaration
    ) =>
        declaration.Default is null
            ? ManifestOperationResult<ResolvedPackParameterValue>.Success(
                CreateOptionalValue(declaration.Type, declaration.Multiple)
            )
            : ParseCompositeValue(name, declaration, declaration.Default);

    private static ManifestOperationResult<ResolvedPackParameterValue> ParseCompositeValue(
        string name,
        PackParameterDefinition declaration,
        object value
    )
    {
        if (declaration.Type == PackParameterType.Bool && value is bool booleanValue)
        {
            return ManifestOperationResult<ResolvedPackParameterValue>.Success(
                new ResolvedPackParameterValue(declaration.Type, string.Empty, booleanValue)
            );
        }

        if (declaration.Multiple && TryGetStringValues(value, out var stringValues))
        {
            return CreateStringValues(name, declaration, stringValues, "Composite parameter");
        }

        return declaration.Type != PackParameterType.Bool && value is string stringValue
            ? CreateStringValue(name, declaration, stringValue, "Composite parameter")
            : ManifestOperationResult<ResolvedPackParameterValue>.Failure(
                $"Composite parameter '{name}' has a type incompatible with its declaration."
            );
    }

    private static ManifestOperationResult<ResolvedPackParameterValue> ParseProjectVariableValue(
        string name,
        PackParameterDefinition declaration,
        object variable
    )
    {
        if (declaration.Type == PackParameterType.Bool && variable is bool booleanValue)
        {
            return ManifestOperationResult<ResolvedPackParameterValue>.Success(
                new ResolvedPackParameterValue(declaration.Type, string.Empty, booleanValue)
            );
        }

        if (declaration.Multiple && TryGetStringValues(variable, out var stringValues))
        {
            return CreateStringValues(name, declaration, stringValues, "Project variable");
        }

        if (declaration.Type != PackParameterType.Bool && variable is string stringValue)
        {
            return CreateStringValue(name, declaration, stringValue, "Project variable");
        }

        return ManifestOperationResult<ResolvedPackParameterValue>.Failure(
            $"Project variable '{name}' has a type incompatible with its parameter declaration."
        );
    }

    private static ManifestOperationResult<ResolvedPackParameterValue> CreateStringValue(
        string name,
        PackParameterDefinition declaration,
        string value,
        string source
    )
    {
        var isInvalidEnumValue =
            declaration.Type == PackParameterType.Enum
            && !declaration.Values.Contains(value, StringComparer.Ordinal);
        if (isInvalidEnumValue)
        {
            return ManifestOperationResult<ResolvedPackParameterValue>.Failure(
                $"{source} '{name}' must be one of: {string.Join(", ", declaration.Values)}."
            );
        }

        return ManifestOperationResult<ResolvedPackParameterValue>.Success(
            new ResolvedPackParameterValue(declaration.Type, value, false)
        );
    }

    private static ManifestOperationResult<ResolvedPackParameterValue> CreateStringValues(
        string name,
        PackParameterDefinition declaration,
        IReadOnlyList<string> values,
        string source
    )
    {
        if (values.Distinct(StringComparer.Ordinal).Count() != values.Count)
        {
            return ManifestOperationResult<ResolvedPackParameterValue>.Failure(
                $"{source} '{name}' cannot contain duplicate values."
            );
        }

        var invalidValue = values.FirstOrDefault(value =>
            !declaration.Values.Contains(value, StringComparer.Ordinal)
        );
        if (invalidValue is not null)
        {
            return ManifestOperationResult<ResolvedPackParameterValue>.Failure(
                $"{source} '{name}' must contain only: {string.Join(", ", declaration.Values)}."
            );
        }

        return ManifestOperationResult<ResolvedPackParameterValue>.Success(
            new ResolvedPackParameterValue(declaration.Type, string.Empty, false, values)
        );
    }

    private static bool TryGetStringValues(object value, out IReadOnlyList<string> stringValues)
    {
        if (value is not IEnumerable<object> values)
        {
            stringValues = [];
            return false;
        }

        var materializedValues = values.OfType<string>().ToList();
        if (materializedValues.Count != values.Count())
        {
            stringValues = [];
            return false;
        }

        stringValues = materializedValues;
        return true;
    }

    private static ResolvedPackParameterValue CreateOptionalValue(
        PackParameterType type,
        bool multiple = false
    ) => new(type, string.Empty, false, multiple ? [] : null);
}
