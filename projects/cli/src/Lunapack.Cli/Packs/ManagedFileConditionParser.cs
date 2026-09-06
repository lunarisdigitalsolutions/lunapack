using Lunapack.Cli.Application.CommandExecution;

namespace Lunapack.Cli.Packs;

internal static class ManagedFileConditionParser
{
    public static ManifestOperationResult<ManagedFileCondition> Parse(
        string condition,
        IReadOnlyDictionary<string, PackParameterDefinition> declarations
    ) => Parse(condition, declarations, allowLifecycleFunctions: false);

    public static ManifestOperationResult<ManagedFileCondition> ParseLifecycle(
        string condition,
        IReadOnlyDictionary<string, PackParameterDefinition> declarations
    ) => Parse(condition, declarations, allowLifecycleFunctions: true);

    public static ManifestOperationResult<PackParameterBindingExpression> ParseBinding(
        string binding,
        IReadOnlyDictionary<string, PackParameterDefinition> declarations
    )
    {
        var trimmed = binding.Trim();
        if (
            !trimmed.StartsWith("${{", StringComparison.Ordinal)
            || !trimmed.EndsWith("}}", StringComparison.Ordinal)
        )
        {
            return ManifestOperationResult<PackParameterBindingExpression>.Failure(
                "Parameter binding expression must use the complete '${{ expression }}' form."
            );
        }

        var expression = trimmed[3..^2].Trim();
        var tokens = Tokenize(expression);
        return tokens.Value is { } parsedTokens
            ? new Parser(parsedTokens, declarations, allowLifecycleFunctions: false).ParseBinding()
            : ManifestOperationResult<PackParameterBindingExpression>.Failure(
                tokens.Error ?? "Unable to parse parameter binding expression."
            );
    }

    public static bool IsBindingExpression(string value)
    {
        var trimmed = value.Trim();
        return trimmed.StartsWith("${{", StringComparison.Ordinal)
            && (
                !trimmed.Contains("}}", StringComparison.Ordinal)
                || trimmed.EndsWith("}}", StringComparison.Ordinal)
            );
    }

    private static ManifestOperationResult<ManagedFileCondition> Parse(
        string condition,
        IReadOnlyDictionary<string, PackParameterDefinition> declarations,
        bool allowLifecycleFunctions
    )
    {
        var tokens = Tokenize(condition);
        if (tokens.Value is not { } parsedTokens)
        {
            return ManifestOperationResult<ManagedFileCondition>.Failure(
                tokens.Error ?? "Unable to parse managed-file condition."
            );
        }

        return new Parser(parsedTokens, declarations, allowLifecycleFunctions).Parse();
    }

    private static ManifestOperationResult<IReadOnlyList<Token>> Tokenize(string condition)
    {
        var tokens = new List<Token>();
        var index = 0;
        while (index < condition.Length)
        {
            if (char.IsWhiteSpace(condition[index]))
            {
                index++;
                continue;
            }

            var position = index;
            if (IsIdentifierStart(condition[index]))
            {
                tokens.Add(ReadIdentifier(condition, ref index));
                continue;
            }

            if (condition[index] == '"')
            {
                var literal = ReadStringLiteral(condition, ref index);
                if (literal.Value is not { } literalToken)
                {
                    return ManifestOperationResult<IReadOnlyList<Token>>.Failure(
                        literal.Error
                            ?? $"Condition contains an unterminated string literal at position {position}."
                    );
                }

                tokens.Add(literalToken);
                continue;
            }

            var token = ReadSymbolicToken(condition, index);
            if (token.Kind == TokenKind.Invalid)
            {
                return ManifestOperationResult<IReadOnlyList<Token>>.Failure(
                    $"Condition contains unexpected token '{token.Text}' at position {position}."
                );
            }

            tokens.Add(token);
            index += token.Text.Length;
        }

        tokens.Add(new Token(TokenKind.End, string.Empty, condition.Length));
        return ManifestOperationResult<IReadOnlyList<Token>>.Success(tokens);
    }

    private static Token ReadIdentifier(string condition, ref int index)
    {
        var position = index;
        index++;
        while (index < condition.Length && IsIdentifierPart(condition[index]))
        {
            index++;
        }

        var identifier = condition[position..index];
        return new Token(
            string.Equals(identifier, "in", StringComparison.Ordinal)
                ? TokenKind.In
                : TokenKind.Identifier,
            identifier,
            position
        );
    }

    private static ManifestOperationResult<Token> ReadStringLiteral(string condition, ref int index)
    {
        var position = index;
        index++;
        var valueStart = index;
        while (index < condition.Length && condition[index] != '"')
        {
            index++;
        }

        if (index == condition.Length)
        {
            return ManifestOperationResult<Token>.Failure(
                $"Condition contains an unterminated string literal at position {position}."
            );
        }

        var token = new Token(TokenKind.StringLiteral, condition[valueStart..index], position);
        index++;
        return ManifestOperationResult<Token>.Success(token);
    }

    private static Token ReadSymbolicToken(string condition, int position) =>
        condition[position..] switch
        {
            var remaining when remaining.StartsWith("&&", StringComparison.Ordinal) => new Token(
                TokenKind.And,
                "&&",
                position
            ),
            var remaining when remaining.StartsWith("||", StringComparison.Ordinal) => new Token(
                TokenKind.Or,
                "||",
                position
            ),
            var remaining when remaining.StartsWith("==", StringComparison.Ordinal) => new Token(
                TokenKind.Equal,
                "==",
                position
            ),
            var remaining when remaining.StartsWith("!=", StringComparison.Ordinal) => new Token(
                TokenKind.NotEqual,
                "!=",
                position
            ),
            var remaining when remaining[0] == '!' => new Token(TokenKind.Not, "!", position),
            var remaining when remaining[0] == '(' => new Token(
                TokenKind.OpenParenthesis,
                "(",
                position
            ),
            var remaining when remaining[0] == ')' => new Token(
                TokenKind.CloseParenthesis,
                ")",
                position
            ),
            var remaining when remaining[0] == ',' => new Token(TokenKind.Comma, ",", position),
            _ => new Token(TokenKind.Invalid, condition[position].ToString(), position),
        };

    private static bool IsIdentifierPart(char character) =>
        IsIdentifierStart(character) || character is >= '0' and <= '9';

    private static bool IsIdentifierStart(char character) =>
        character is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or '_';

    private sealed class Parser(
        IReadOnlyList<Token> tokens,
        IReadOnlyDictionary<string, PackParameterDefinition> declarations,
        bool allowLifecycleFunctions
    )
    {
        private int _position;
        private string? _error;

        public ManifestOperationResult<ManagedFileCondition> Parse()
        {
            var expression = ParseOrExpression();
            if (expression is null)
            {
                return ManifestOperationResult<ManagedFileCondition>.Failure(
                    _error ?? "Invalid managed-file condition."
                );
            }

            if (Current.Kind != TokenKind.End)
            {
                return ManifestOperationResult<ManagedFileCondition>.Failure(
                    $"Condition contains unexpected token '{Current.Text}' at position {Current.Position}."
                );
            }

            return ManifestOperationResult<ManagedFileCondition>.Success(expression);
        }

        public ManifestOperationResult<PackParameterBindingExpression> ParseBinding()
        {
            var expression = ParseValueExpression();
            if (expression is null)
            {
                return ManifestOperationResult<PackParameterBindingExpression>.Failure(
                    _error ?? "Invalid parameter binding expression."
                );
            }

            if (Current.Kind != TokenKind.End)
            {
                return ManifestOperationResult<PackParameterBindingExpression>.Failure(
                    $"Parameter binding expression contains unexpected token '{Current.Text}' at position {Current.Position}."
                );
            }

            return ManifestOperationResult<PackParameterBindingExpression>.Success(expression);
        }

        private PackParameterBindingExpression? ParseValueExpression()
        {
            if (Current.Kind == TokenKind.StringLiteral)
            {
                var value = Current.Text;
                _position++;
                return new(_ => value, PackParameterType.String, false, CreateReferences());
            }

            if (Current.Kind != TokenKind.Identifier)
            {
                _error =
                    $"Parameter binding expression requires a value at position {Current.Position}.";
                return null;
            }

            if (string.Equals(Current.Text, "iif", StringComparison.Ordinal))
            {
                return ParseConditionalValue();
            }

            if (Current.Text is "true" or "false")
            {
                var value = string.Equals(Current.Text, "true", StringComparison.Ordinal);
                _position++;
                return new(_ => value, PackParameterType.Bool, false, CreateReferences());
            }

            var parameter = Current;
            _position++;
            if (!TryGetDeclaration(parameter, null, out var declaration))
            {
                return null;
            }

            return new(
                values => values[parameter.Text].Value,
                declaration.Type,
                declaration.Multiple,
                CreateReferences(parameter.Text)
            );
        }

        private PackParameterBindingExpression? ParseConditionalValue()
        {
            _position++;
            if (!Match(TokenKind.OpenParenthesis))
            {
                _error =
                    $"Parameter binding function 'iif' requires '(' at position {Current.Position}.";
                return null;
            }

            var condition = ParseOrExpression();
            if (condition is null || !Match(TokenKind.Comma))
            {
                _error ??=
                    $"Parameter binding function 'iif' requires a condition followed by ',' at position {Current.Position}.";
                return null;
            }

            var whenTrue = ParseValueExpression();
            if (whenTrue is null || !Match(TokenKind.Comma))
            {
                _error ??=
                    $"Parameter binding function 'iif' requires a true value followed by ',' at position {Current.Position}.";
                return null;
            }

            var whenFalse = ParseValueExpression();
            if (whenFalse is null || !Match(TokenKind.CloseParenthesis))
            {
                _error ??=
                    $"Parameter binding function 'iif' requires a false value followed by ')' at position {Current.Position}.";
                return null;
            }

            if (!HaveCompatibleTypes(whenTrue, whenFalse))
            {
                _error = "Parameter binding function 'iif' requires compatible branch types.";
                return null;
            }

            var references = condition
                .ReferencedParameters.Concat(whenTrue.ReferencedParameters)
                .Concat(whenFalse.ReferencedParameters)
                .ToHashSet(StringComparer.Ordinal);
            return new(
                values =>
                    condition.Evaluate(values)
                        ? EvaluateResolved(whenTrue, values)
                        : EvaluateResolved(whenFalse, values),
                GetCompatibleType(whenTrue, whenFalse),
                whenTrue.Multiple,
                references
            );
        }

        private static object EvaluateResolved(
            PackParameterBindingExpression expression,
            IReadOnlyDictionary<string, ResolvedPackParameterValue> values
        ) =>
            expression.Evaluate(values).Value is { } value
                ? value
                : throw new InvalidOperationException(
                    "A validated parameter binding branch could not be evaluated."
                );

        private static bool HaveCompatibleTypes(
            PackParameterBindingExpression left,
            PackParameterBindingExpression right
        ) =>
            left.Multiple == right.Multiple
            && (
                left.Type == right.Type
                || (
                    !left.Multiple
                    && left.Type is PackParameterType.String or PackParameterType.Enum
                    && right.Type is PackParameterType.String or PackParameterType.Enum
                )
            );

        private static PackParameterType GetCompatibleType(
            PackParameterBindingExpression left,
            PackParameterBindingExpression right
        ) => left.Type == right.Type ? left.Type : PackParameterType.String;

        private ManagedFileCondition? ParseOrExpression()
        {
            var expression = ParseAndExpression();
            while (expression is not null && Match(TokenKind.Or))
            {
                var right = ParseAndExpression();
                if (right is null)
                {
                    return null;
                }

                var left = expression;
                expression = new ManagedFileCondition(
                    (values, context) =>
                        left.Evaluate(values, context) || right.Evaluate(values, context),
                    CombineReferences(left, right),
                    left.DependsOnRuntimeState || right.DependsOnRuntimeState,
                    left.DependsOnPreviousScriptState || right.DependsOnPreviousScriptState
                );
            }

            return expression;
        }

        private ManagedFileCondition? ParseAndExpression()
        {
            var expression = ParsePrimaryExpression();
            while (expression is not null && Match(TokenKind.And))
            {
                var right = ParsePrimaryExpression();
                if (right is null)
                {
                    return null;
                }

                var left = expression;
                expression = new ManagedFileCondition(
                    (values, context) =>
                        left.Evaluate(values, context) && right.Evaluate(values, context),
                    CombineReferences(left, right),
                    left.DependsOnRuntimeState || right.DependsOnRuntimeState,
                    left.DependsOnPreviousScriptState || right.DependsOnPreviousScriptState
                );
            }

            return expression;
        }

        private ManagedFileCondition? ParsePrimaryExpression()
        {
            if (Match(TokenKind.OpenParenthesis))
            {
                var expression = ParseOrExpression();
                if (expression is null || !Match(TokenKind.CloseParenthesis))
                {
                    _error ??= $"Condition requires ')' at position {Current.Position}.";
                    return null;
                }

                return expression;
            }

            if (Current.Kind == TokenKind.StringLiteral)
            {
                return ParseMembership();
            }

            var negated = Match(TokenKind.Not);
            if (
                Current.Kind == TokenKind.Identifier
                && string.Equals(Current.Text, "isDefault", StringComparison.Ordinal)
            )
            {
                return ParseDefaultPredicate(negated);
            }

            if (
                Current.Kind == TokenKind.Identifier
                && Current.Text is "scriptsSkipped" or "previousScriptState"
            )
            {
                return ParseLifecycleFunction(negated);
            }

            if (Current.Kind != TokenKind.Identifier)
            {
                _error = $"Condition requires a parameter name at position {Current.Position}.";
                return null;
            }

            var parameter = Current;
            _position++;
            if (Current.Kind is TokenKind.Equal or TokenKind.NotEqual)
            {
                if (negated)
                {
                    _error = $"Condition cannot negate comparison for '{parameter.Text}'.";
                    return null;
                }

                return ParseComparison(parameter);
            }

            return ParseBooleanParameter(parameter, negated);
        }

        private ManagedFileCondition? ParseLifecycleFunction(bool negated)
        {
            var function = Current;
            if (!allowLifecycleFunctions)
            {
                _error = $"Condition function '{function.Text}' is only valid for lifecycle hooks.";
                return null;
            }

            _position++;
            if (!Match(TokenKind.OpenParenthesis) || !Match(TokenKind.CloseParenthesis))
            {
                _error = $"Condition function '{function.Text}' does not accept arguments.";
                return null;
            }

            if (string.Equals(function.Text, "scriptsSkipped", StringComparison.Ordinal))
            {
                return new ManagedFileCondition(
                    (_, context) => negated ? !context.ScriptsSkipped : context.ScriptsSkipped,
                    dependsOnRuntimeState: true
                );
            }

            if (negated || Current.Kind is not (TokenKind.Equal or TokenKind.NotEqual))
            {
                _error = "Condition must compare 'previousScriptState()' with a supported state.";
                return null;
            }

            var comparison = Current.Kind;
            _position++;
            if (Current.Kind != TokenKind.StringLiteral)
            {
                _error = "Condition must compare 'previousScriptState()' with a quoted state.";
                return null;
            }

            if (!TryParseState(Current.Text, out var state))
            {
                _error = $"Condition contains unsupported previous script state '{Current.Text}'.";
                return null;
            }

            _position++;
            return new ManagedFileCondition(
                (_, context) =>
                    comparison == TokenKind.Equal
                        ? context.PreviousScriptState == state
                        : context.PreviousScriptState != state,
                dependsOnRuntimeState: true,
                dependsOnPreviousScriptState: true
            );
        }

        private static bool TryParseState(string value, out LifecycleScriptState state) =>
            Enum.TryParse(value, ignoreCase: true, out state)
            && string.Equals(value, state.ToString(), StringComparison.OrdinalIgnoreCase);

        private ManagedFileCondition? ParseDefaultPredicate(bool negated)
        {
            _position++;
            if (!Match(TokenKind.OpenParenthesis) || Current.Kind != TokenKind.Identifier)
            {
                _error =
                    $"Condition requires a parameter name in 'isDefault' at position {Current.Position}.";
                return null;
            }

            var parameter = Current;
            _position++;
            if (!Match(TokenKind.CloseParenthesis))
            {
                _error = $"Condition requires ')' at position {Current.Position}.";
                return null;
            }

            if (!TryGetDeclaration(parameter, null, out var declaration))
            {
                return null;
            }

            if (declaration.Default is null)
            {
                _error =
                    $"Condition default check requires parameter '{parameter.Text}' to declare a default value.";
                return null;
            }

            return new ManagedFileCondition(
                values =>
                {
                    var isDefault =
                        values.TryGetValue(parameter.Text, out var value)
                        && IsDefaultValue(value, declaration);
                    return negated ? !isDefault : isDefault;
                },
                CreateReferences(parameter.Text)
            );
        }

        private static bool IsDefaultValue(
            ResolvedPackParameterValue value,
            PackParameterDefinition declaration
        )
        {
            if (declaration.Type == PackParameterType.Bool)
            {
                return declaration.Default is bool defaultValue
                    && value.BooleanValue == defaultValue;
            }

            if (!declaration.Multiple)
            {
                return declaration.Default is string defaultValue
                    && string.Equals(value.StringValue, defaultValue, StringComparison.Ordinal);
            }

            return declaration.Default is IEnumerable<object> defaultValues
                && value.StringValues is { } resolvedValues
                && resolvedValues.SequenceEqual(
                    defaultValues.OfType<string>(),
                    StringComparer.Ordinal
                )
                && defaultValues.All(defaultValue => defaultValue is string);
        }

        private ManagedFileCondition? ParseMembership()
        {
            var literal = Current.Text;
            _position++;
            if (!Match(TokenKind.In) || Current.Kind != TokenKind.Identifier)
            {
                _error =
                    $"Condition requires 'in' followed by a parameter name at position {Current.Position}.";
                return null;
            }

            var parameter = Current;
            _position++;
            if (!TryGetDeclaration(parameter, PackParameterType.Enum, out var declaration))
            {
                return null;
            }

            if (!declaration.Multiple)
            {
                _error =
                    $"Condition membership requires a multi-select enum parameter but '{parameter.Text}' is scalar.";
                return null;
            }

            return new ManagedFileCondition(
                values =>
                    values.TryGetValue(parameter.Text, out var value)
                    && value.StringValues is { } selections
                    && selections.Contains(literal, StringComparer.Ordinal),
                CreateReferences(parameter.Text)
            );
        }

        private ManagedFileCondition? ParseBooleanParameter(Token parameter, bool negated)
        {
            if (!TryGetDeclaration(parameter, PackParameterType.Bool, out _))
            {
                return null;
            }

            return new ManagedFileCondition(
                values =>
                    values.TryGetValue(parameter.Text, out var value)
                    && (negated ? !value.BooleanValue : value.BooleanValue),
                CreateReferences(parameter.Text)
            );
        }

        private ManagedFileCondition? ParseComparison(Token parameter)
        {
            var operatorToken = Current;
            _position++;
            if (Current.Kind != TokenKind.StringLiteral)
            {
                _error =
                    $"Condition requires a quoted string literal at position {Current.Position}.";
                return null;
            }

            var literal = Current.Text;
            _position++;
            if (!TryGetDeclaration(parameter, null, out var declaration))
            {
                return null;
            }

            if (declaration.Type == PackParameterType.Bool)
            {
                _error =
                    $"Condition cannot compare boolean parameter '{parameter.Text}' to a string literal.";
                return null;
            }

            if (declaration.Multiple)
            {
                _error =
                    $"Condition cannot compare multi-select parameter '{parameter.Text}' to a string literal.";
                return null;
            }

            return new ManagedFileCondition(
                values =>
                    values.TryGetValue(parameter.Text, out var value)
                    && (
                        operatorToken.Kind == TokenKind.Equal
                            ? string.Equals(value.StringValue, literal, StringComparison.Ordinal)
                            : !string.Equals(value.StringValue, literal, StringComparison.Ordinal)
                    ),
                CreateReferences(parameter.Text)
            );
        }

        private static HashSet<string> CombineReferences(
            ManagedFileCondition left,
            ManagedFileCondition right
        ) =>
            left
                .ReferencedParameters.Concat(right.ReferencedParameters)
                .ToHashSet(StringComparer.Ordinal);

        private static HashSet<string> CreateReferences(params string[] parameters) =>
            new(parameters, StringComparer.Ordinal);

        private bool TryGetDeclaration(
            Token parameter,
            PackParameterType? expectedType,
            [System.Diagnostics.CodeAnalysis.NotNullWhen(true)]
                out PackParameterDefinition? declaration
        )
        {
            if (!declarations.TryGetValue(parameter.Text, out declaration))
            {
                _error = $"Condition references undeclared parameter '{parameter.Text}'.";
                return false;
            }

            if (expectedType is not null && declaration.Type != expectedType)
            {
                _error =
                    $"Condition requires a boolean parameter but '{parameter.Text}' is {GetTypeName(declaration.Type)}.";
                return false;
            }

            return true;
        }

        private static string GetTypeName(PackParameterType type) =>
            type switch
            {
                PackParameterType.Bool => "bool",
                PackParameterType.Enum => "enum",
                _ => "string",
            };

        private Token Current => tokens[_position];

        private bool Match(TokenKind kind)
        {
            if (Current.Kind != kind)
            {
                return false;
            }

            _position++;
            return true;
        }
    }

    private sealed record Token(TokenKind Kind, string Text, int Position);

    private enum TokenKind
    {
        Identifier,
        StringLiteral,
        And,
        Or,
        Not,
        Equal,
        NotEqual,
        In,
        OpenParenthesis,
        CloseParenthesis,
        Comma,
        End,
        Invalid,
    }
}
