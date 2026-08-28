namespace Lunapack.Cli.UnitTests;

public sealed class ManagedFileConditionParserTests
{
    [Test]
    public async Task Parse_WhenBooleanOperatorsAndNegationUsed_EvaluatesCondition()
    {
        var result = new ManagedFileConditionParser().Parse(
            "includeCi && !includeSecurity",
            CreateDeclarations()
        );

        await Assert.That(result.IsSuccess).IsTrue();
        await Assert
            .That(
                result
                    .RequireValue()
                    .Evaluate(CreateValues(includeCi: true, includeSecurity: false))
            )
            .IsTrue();
    }

    [Test]
    public async Task Parse_WhenStringEnumComparisonAndParenthesesUsed_EvaluatesCondition()
    {
        var result = new ManagedFileConditionParser().Parse(
            "(licenseKind == \"mit\" || environment != \"production\") && includeCi",
            CreateDeclarations()
        );

        await Assert.That(result.IsSuccess).IsTrue();
        await Assert
            .That(
                result
                    .RequireValue()
                    .Evaluate(CreateValues(includeCi: true, licenseKind: "apache-2.0"))
            )
            .IsTrue();
    }

    [Test]
    public async Task Parse_WhenMembershipCombined_EvaluatesSelectedValues()
    {
        var result = new ManagedFileConditionParser().Parse(
            "\"api\" in features && (\"docker\" in features || includeCi)",
            CreateDeclarations()
        );

        await Assert.That(result.IsSuccess).IsTrue();
        await Assert
            .That(
                result
                    .RequireValue()
                    .Evaluate(CreateValues(includeCi: false, features: ["api", "docker"]))
            )
            .IsTrue();
    }

    [Test]
    public async Task Parse_WhenMembershipValueAbsent_EvaluatesFalse()
    {
        var result = new ManagedFileConditionParser().Parse(
            "\"docker\" in features",
            CreateDeclarations()
        );

        await Assert.That(result.IsSuccess).IsTrue();
        await Assert
            .That(result.RequireValue().Evaluate(CreateValues(includeCi: false, features: [])))
            .IsFalse();
    }

    [Test]
    public async Task Parse_WhenMembershipUsesScalarEnum_ReturnsFailure()
    {
        var result = new ManagedFileConditionParser().Parse(
            "\"mit\" in licenseKind",
            CreateDeclarations()
        );

        await Assert.That(result.IsSuccess).IsFalse();
    }

    [Test]
    public async Task Parse_WhenMultiSelectUsesEquality_ReturnsFailure()
    {
        var result = new ManagedFileConditionParser().Parse(
            "features == \"api\"",
            CreateDeclarations()
        );

        await Assert.That(result.IsSuccess).IsFalse();
    }

    [Test]
    public async Task Parse_WhenParameterUndeclared_ReturnsFailure()
    {
        var result = new ManagedFileConditionParser().Parse("unknown", CreateDeclarations());

        await Assert.That(result.IsSuccess).IsFalse();
    }

    [Test]
    public async Task Parse_WhenBooleanComparedToString_ReturnsFailure()
    {
        var result = new ManagedFileConditionParser().Parse(
            "includeCi == \"true\"",
            CreateDeclarations()
        );

        await Assert.That(result.IsSuccess).IsFalse();
    }

    [Test]
    public async Task Parse_WhenStringUsedAsBoolean_ReturnsFailure()
    {
        var result = new ManagedFileConditionParser().Parse("environment", CreateDeclarations());

        await Assert.That(result.IsSuccess).IsFalse();
    }

    [Test]
    public async Task Parse_WhenSyntaxInvalid_ReturnsFailure()
    {
        var result = new ManagedFileConditionParser().Parse("includeCi &&", CreateDeclarations());

        await Assert.That(result.IsSuccess).IsFalse();
    }

    private static Dictionary<string, PackParameterDefinition> CreateDeclarations() =>
        new Dictionary<string, PackParameterDefinition>(StringComparer.Ordinal)
        {
            ["environment"] = new(PackParameterType.String, false, []),
            ["includeCi"] = new(PackParameterType.Bool, false, []),
            ["includeSecurity"] = new(PackParameterType.Bool, false, []),
            ["licenseKind"] = new(PackParameterType.Enum, false, ["mit", "apache-2.0"]),
            ["features"] = new(PackParameterType.Enum, false, ["api", "docker"], Multiple: true),
        };

    private static Dictionary<string, ResolvedPackParameterValue> CreateValues(
        bool includeCi,
        bool includeSecurity = false,
        string licenseKind = "mit",
        string environment = "development",
        IReadOnlyList<string>? features = null
    ) =>
        new Dictionary<string, ResolvedPackParameterValue>(StringComparer.Ordinal)
        {
            ["environment"] = new(PackParameterType.String, environment, false),
            ["includeCi"] = new(PackParameterType.Bool, string.Empty, includeCi),
            ["includeSecurity"] = new(PackParameterType.Bool, string.Empty, includeSecurity),
            ["licenseKind"] = new(PackParameterType.Enum, licenseKind, false),
            ["features"] = new(PackParameterType.Enum, string.Empty, false, features ?? []),
        };
}
