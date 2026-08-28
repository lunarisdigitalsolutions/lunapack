namespace Lunapack.Cli;

internal sealed class ManagedFileConditionParser
{
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Performance",
        "CA1822:Mark members as static",
        Justification = "Parser remains injectable by the installation planner."
    )]
    public ManifestOperationResult<ManagedFileCondition> Parse(
        string condition,
        IReadOnlyDictionary<string, PackParameterDefinition> declarations
    )
    {
        var tokens = Tokenize(condition);
        if (tokens.Value is not { } parsedTokens)
        {
            return ManifestOperationResult<ManagedFileCondition>.Failure(
                tokens.Error ?? "Unable to parse managed-file condition."
            );
        }

        return new Parser(parsedTokens, declarations).Parse();
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Maintainability",
        "MA0051:Method is too long",
        Justification = "Token recognition order is a single parser invariant."
    )]
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
                index++;
                while (index < condition.Length && IsIdentifierPart(condition[index]))
                {
                    index++;
                }

                var identifier = condition[position..index];
                tokens.Add(
                    new Token(
                        string.Equals(identifier, "in", StringComparison.Ordinal)
                            ? TokenKind.In
                            : TokenKind.Identifier,
                        identifier,
                        position
                    )
                );
                continue;
            }

            if (condition[index] == '"')
            {
                index++;
                var valueStart = index;
                while (index < condition.Length && condition[index] != '"')
                {
                    index++;
                }

                if (index == condition.Length)
                {
                    return ManifestOperationResult<IReadOnlyList<Token>>.Failure(
                        $"Condition contains an unterminated string literal at position {position}."
                    );
                }

                tokens.Add(
                    new Token(TokenKind.StringLiteral, condition[valueStart..index], position)
                );
                index++;
                continue;
            }

            var token = condition[index..] switch
            {
                var remaining when remaining.StartsWith("&&", StringComparison.Ordinal) =>
                    new Token(TokenKind.And, "&&", position),
                var remaining when remaining.StartsWith("||", StringComparison.Ordinal) =>
                    new Token(TokenKind.Or, "||", position),
                var remaining when remaining.StartsWith("==", StringComparison.Ordinal) =>
                    new Token(TokenKind.Equal, "==", position),
                var remaining when remaining.StartsWith("!=", StringComparison.Ordinal) =>
                    new Token(TokenKind.NotEqual, "!=", position),
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
                _ => new Token(TokenKind.Invalid, condition[index].ToString(), position),
            };
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

    private static bool IsIdentifierPart(char character) =>
        IsIdentifierStart(character) || character is >= '0' and <= '9';

    private static bool IsIdentifierStart(char character) =>
        character is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or '_';

    private sealed class Parser(
        IReadOnlyList<Token> tokens,
        IReadOnlyDictionary<string, PackParameterDefinition> declarations
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
                expression = new ManagedFileCondition(values =>
                    left.Evaluate(values) || right.Evaluate(values)
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
                expression = new ManagedFileCondition(values =>
                    left.Evaluate(values) && right.Evaluate(values)
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

            return new ManagedFileCondition(values =>
                values.TryGetValue(parameter.Text, out var value)
                && value.StringValues is { } selections
                && selections.Contains(literal, StringComparer.Ordinal)
            );
        }

        private ManagedFileCondition? ParseBooleanParameter(Token parameter, bool negated)
        {
            if (!TryGetDeclaration(parameter, PackParameterType.Bool, out _))
            {
                return null;
            }

            return new ManagedFileCondition(values =>
                values.TryGetValue(parameter.Text, out var value)
                && (negated ? !value.BooleanValue : value.BooleanValue)
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

            return new ManagedFileCondition(values =>
                values.TryGetValue(parameter.Text, out var value)
                && (
                    operatorToken.Kind == TokenKind.Equal
                        ? string.Equals(value.StringValue, literal, StringComparison.Ordinal)
                        : !string.Equals(value.StringValue, literal, StringComparison.Ordinal)
                )
            );
        }

        private bool TryGetDeclaration(
            Token parameter,
            PackParameterType? expectedType,
            out PackParameterDefinition declaration
        )
        {
            if (!declarations.TryGetValue(parameter.Text, out declaration!))
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
        End,
        Invalid,
    }
}
