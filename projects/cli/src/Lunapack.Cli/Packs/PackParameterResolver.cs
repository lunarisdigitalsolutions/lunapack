namespace Lunapack.Cli;

internal static class PackParameterResolver
{
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
            installationRequest
        );
    }

    public static ManifestOperationResult<
        IReadOnlyList<PackParameterPrompt>
    > FindUnresolvedRequired(
        ResolvedPackGraph graph,
        ProjectConfiguration configuration,
        PackInstallationRequest installationRequest
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
        var unresolved = resolvedDeclarations
            .Where(declaration =>
                declaration.Value.Required
                && !resolvedCompositeValues.ContainsKey(declaration.Key)
                && !inputValues.ContainsKey(declaration.Key)
                && !(
                    installationRequest.UseProjectVariables
                    && !installationRequest.SkippedVariables.Contains(declaration.Key)
                    && configuration.Variables.ContainsKey(declaration.Key)
                )
            )
            .Select(declaration => new PackParameterPrompt(declaration.Key, declaration.Value))
            .ToList();

        return ManifestOperationResult<IReadOnlyList<PackParameterPrompt>>.Success(unresolved);
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
                    if (
                        existingDeclaration.Type != parameterDeclaration.Type
                        || existingDeclaration.Multiple != parameterDeclaration.Multiple
                    )
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

    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Maintainability",
        "MA0051:Method is too long",
        Justification = "Parameter precedence remains explicit in one ordered resolution workflow."
    )]
    private static ManifestOperationResult<ResolvedPackParameters> BindValues(
        IReadOnlyDictionary<string, PackParameterDefinition> declarations,
        IReadOnlyDictionary<string, object> compositeValues,
        ProjectConfiguration configuration,
        PackInstallationRequest installationRequest
    )
    {
        foreach (var skippedVariable in installationRequest.SkippedVariables)
        {
            if (compositeValues.ContainsKey(skippedVariable))
            {
                return ManifestOperationResult<ResolvedPackParameters>.Failure(
                    $"Parameter '{skippedVariable}' is fixed by a composite pack."
                );
            }

            if (!declarations.ContainsKey(skippedVariable))
            {
                return ManifestOperationResult<ResolvedPackParameters>.Failure(
                    $"Project variable '{skippedVariable}' is not declared by the resolved pack graph."
                );
            }
        }

        var resolvedValues = new Dictionary<string, ResolvedPackParameterValue>(
            StringComparer.Ordinal
        );
        foreach (var (name, value) in compositeValues)
        {
            var compositeValue = ParseCompositeValue(name, declarations[name], value);
            if (compositeValue.Value is not { } resolvedValue)
            {
                return ManifestOperationResult<ResolvedPackParameters>.Failure(
                    compositeValue.Error ?? $"Invalid composite value for parameter '{name}'."
                );
            }

            resolvedValues.Add(name, resolvedValue);
        }

        foreach (var (name, values) in installationRequest.GetParameterValues())
        {
            if (!declarations.TryGetValue(name, out var declaration))
            {
                return ManifestOperationResult<ResolvedPackParameters>.Failure(
                    $"Parameter '{name}' is not declared by the resolved pack graph."
                );
            }

            if (compositeValues.ContainsKey(name))
            {
                return ManifestOperationResult<ResolvedPackParameters>.Failure(
                    $"Parameter '{name}' is fixed by a composite pack."
                );
            }

            var commandLineValue = ParseCommandLineValue(name, declaration, values);
            if (commandLineValue.Value is not { } resolvedValue)
            {
                return ManifestOperationResult<ResolvedPackParameters>.Failure(
                    commandLineValue.Error ?? $"Invalid value for parameter '{name}'."
                );
            }

            resolvedValues.Add(name, resolvedValue);
        }

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
                    return ManifestOperationResult<ResolvedPackParameters>.Failure(
                        projectValue.Error ?? $"Invalid project variable '{name}'."
                    );
                }

                resolvedValues.Add(name, resolvedValue);
                continue;
            }

            if (declaration.Required)
            {
                return ManifestOperationResult<ResolvedPackParameters>.Failure(
                    $"Required parameter '{name}' has no resolved value."
                );
            }

            var defaultValue = ParseDefaultValue(name, declaration);
            if (defaultValue.Value is not { } resolvedDefault)
            {
                return ManifestOperationResult<ResolvedPackParameters>.Failure(
                    defaultValue.Error ?? $"Invalid default value for parameter '{name}'."
                );
            }

            resolvedValues.Add(name, resolvedDefault);
        }

        return ManifestOperationResult<ResolvedPackParameters>.Success(
            new ResolvedPackParameters(declarations, resolvedValues)
        );
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
                    declaration.Required,
                    values,
                    declaration.DisplayName,
                    declaration.Description,
                    declaration.Default,
                    declaration.Multiple is true
                )
            );
        }

        return values.Count == 0
            ? ManifestOperationResult<PackParameterDefinition>.Success(
                new PackParameterDefinition(
                    type.Value,
                    declaration.Required,
                    [],
                    declaration.DisplayName,
                    declaration.Description,
                    declaration.Default,
                    false
                )
            )
            : ManifestOperationResult<PackParameterDefinition>.Failure(
                $"Parameter '{name}' in pack '{packId}' may only declare values when its type is enum."
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
        if (
            declaration.Type == PackParameterType.Enum
            && !declaration.Values.Contains(value, StringComparer.Ordinal)
        )
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
