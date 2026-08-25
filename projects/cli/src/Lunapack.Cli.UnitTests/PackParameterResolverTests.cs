using System.IO.Abstractions.TestingHelpers;

namespace Lunapack.Cli.UnitTests;

[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Design",
    "MA0002:Use an overload that has a IEqualityComparer<string> or IComparer<string> parameter",
    Justification = "Fixture assertions use TUnit's collection comparison API."
)]
public sealed class PackParameterResolverTests
{
    [Test]
    public async Task FindUnresolvedRequired_WhenRequiredParameterHasNoValue_ReturnsPromptMetadata()
    {
        var pack = CreatePack("license-mit", "string", required: true);
        pack.Manifest.Parameters["companyName"].DisplayName = "Company name";
        pack.Manifest.Parameters["companyName"].Description = "Legal entity name.";

        var result = PackParameterResolver.FindUnresolvedRequired(
            new ResolvedPackGraph([pack]),
            new ProjectConfiguration(),
            CreateRequest()
        );

        await Assert.That(result.IsSuccess).IsTrue();
        var prompt = result.RequireValue().Single();
        await Assert.That(prompt.Id).IsEqualTo("companyName");
        await Assert.That(prompt.Definition.DisplayName).IsEqualTo("Company name");
        await Assert.That(prompt.Definition.Description).IsEqualTo("Legal entity name.");
    }

    [Test]
    public async Task Resolve_WhenExplicitParameterProvided_TakesPrecedenceOverProjectVariable()
    {
        var result = PackParameterResolver.Resolve(
            new ResolvedPackGraph([CreatePack("license-mit", "string", required: true)]),
            new ProjectConfiguration
            {
                Variables = new Dictionary<string, object> { ["companyName"] = "Project Name" },
            },
            CreateRequest(
                parameters: new Dictionary<string, string> { ["companyName"] = "CLI Name" }
            )
        );

        await Assert.That(result.IsSuccess).IsTrue();
        await Assert
            .That(result.RequireValue().Values["companyName"].StringValue)
            .IsEqualTo("CLI Name");
    }

    [Test]
    public async Task Resolve_WhenProjectVariableSkipped_ReturnsMissingRequiredFailure()
    {
        var result = PackParameterResolver.Resolve(
            new ResolvedPackGraph([CreatePack("license-mit", "string", required: true)]),
            new ProjectConfiguration
            {
                Variables = new Dictionary<string, object> { ["companyName"] = "Project Name" },
            },
            CreateRequest(
                skippedVariables: new HashSet<string>(StringComparer.Ordinal) { "companyName" }
            )
        );

        await Assert.That(result.IsSuccess).IsFalse();
    }

    [Test]
    public async Task Resolve_WhenBooleanProjectVariableIsString_ReturnsFailure()
    {
        var result = PackParameterResolver.Resolve(
            new ResolvedPackGraph([CreatePack("ci", "bool", required: true)]),
            new ProjectConfiguration
            {
                Variables = new Dictionary<string, object> { ["companyName"] = "true" },
            },
            CreateRequest()
        );

        await Assert.That(result.IsSuccess).IsFalse();
    }

    [Test]
    public async Task Resolve_WhenEnumValueOutsideAllowedValues_ReturnsFailure()
    {
        var result = PackParameterResolver.Resolve(
            new ResolvedPackGraph([CreatePack("license", "enum", values: ["mit", "apache-2.0"])]),
            new ProjectConfiguration(),
            CreateRequest(
                parameters: new Dictionary<string, string> { ["companyName"] = "bsd-3-clause" }
            )
        );

        await Assert.That(result.IsSuccess).IsFalse();
    }

    [Test]
    public async Task Resolve_WhenSharedDeclarationsCompatible_BindsParameterOnce()
    {
        var result = PackParameterResolver.Resolve(
            new ResolvedPackGraph([
                CreatePack("root", "string", required: true),
                CreatePack("dependency", "string", required: true),
            ]),
            new ProjectConfiguration(),
            CreateRequest(
                parameters: new Dictionary<string, string> { ["companyName"] = "Lunaris" }
            )
        );

        await Assert.That(result.IsSuccess).IsTrue();
        await Assert.That(result.RequireValue().Values).Count().IsEqualTo(1);
    }

    [Test]
    public async Task Resolve_WhenSharedDeclarationsIncompatible_ReturnsFailure()
    {
        var result = PackParameterResolver.Resolve(
            new ResolvedPackGraph([
                CreatePack("root", "string", required: true),
                CreatePack("dependency", "bool", required: true),
            ]),
            new ProjectConfiguration(),
            CreateRequest()
        );

        await Assert.That(result.IsSuccess).IsFalse();
    }

    [Test]
    public async Task Resolve_WhenRootOverridesEnumDeclaration_UsesRootValuesWithoutMerging()
    {
        var dependency = CreatePack("dependency", "enum", values: ["mit", "apache-2.0"]);
        var root = CreatePack("root", "enum", values: ["proprietary"]);
        root.Manifest.Packs.Add(
            new PackManifest.PackReference { Id = "dependency", Version = "1.0.0" }
        );

        var result = PackParameterResolver.Resolve(
            new ResolvedPackGraph([dependency, root]),
            new ProjectConfiguration(),
            CreateRequest(parameters: new Dictionary<string, string> { ["companyName"] = "mit" })
        );

        await Assert.That(result.IsSuccess).IsFalse();
    }

    [Test]
    public async Task Resolve_WhenCompositeSetsTransientParameter_HidesAndBindsValue()
    {
        var dependency = CreatePack("dependency", "string", required: true);
        var root = new DiscoveredPack(
            "source",
            "root",
            new PackManifest
            {
                Id = "root",
                Version = "1.0.0",
                Packs =
                [
                    new PackManifest.PackReference
                    {
                        Id = "dependency",
                        Version = "1.0.0",
                        Parameters = new Dictionary<string, object> { ["companyName"] = "Lunaris" },
                    },
                ],
            }
        );

        var result = PackParameterResolver.Resolve(
            new ResolvedPackGraph([dependency, root]),
            new ProjectConfiguration(),
            CreateRequest()
        );

        await Assert.That(result.IsSuccess).IsTrue();
        await Assert
            .That(result.RequireValue().Values["companyName"].StringValue)
            .IsEqualTo("Lunaris");
    }

    [Test]
    public async Task Resolve_WhenCompositeSetsTransientParameter_RejectsExplicitOverride()
    {
        var dependency = CreatePack("dependency", "string", required: true);
        var root = new DiscoveredPack(
            "source",
            "root",
            new PackManifest
            {
                Id = "root",
                Version = "1.0.0",
                Packs =
                [
                    new PackManifest.PackReference
                    {
                        Id = "dependency",
                        Version = "1.0.0",
                        Parameters = new Dictionary<string, object> { ["companyName"] = "Lunaris" },
                    },
                ],
            }
        );

        var result = PackParameterResolver.Resolve(
            new ResolvedPackGraph([dependency, root]),
            new ProjectConfiguration(),
            CreateRequest(
                parameters: new Dictionary<string, string> { ["companyName"] = "User value" }
            )
        );

        await Assert.That(result.IsSuccess).IsFalse();
    }

    [Test]
    public async Task Resolve_WhenOptionalParametersOmitted_UsesTypedEmptyValues()
    {
        var pack = new DiscoveredPack(
            "source",
            "optional",
            new PackManifest
            {
                Id = "optional",
                Version = "1.0.0",
                Parameters = new Dictionary<string, PackManifest.PackParameter>
                {
                    ["companyName"] = new PackManifest.PackParameter { Type = "string" },
                    ["includeCi"] = new PackManifest.PackParameter { Type = "bool" },
                },
            }
        );
        var result = PackParameterResolver.Resolve(
            new ResolvedPackGraph([pack]),
            new ProjectConfiguration(),
            CreateRequest()
        );

        await Assert.That(result.IsSuccess).IsTrue();
        var values = result.RequireValue().Values;
        await Assert.That(values["companyName"].StringValue).IsEmpty();
        await Assert.That(values["includeCi"].BooleanValue).IsFalse();
    }

    private static DiscoveredPack CreatePack(
        string packId,
        string type,
        bool required = false,
        List<string>? values = null
    ) =>
        new(
            "source",
            packId,
            new PackManifest
            {
                Id = packId,
                Version = "1.0.0",
                Parameters = new Dictionary<string, PackManifest.PackParameter>
                {
                    ["companyName"] = new PackManifest.PackParameter
                    {
                        Required = required,
                        Type = type,
                        Values = values ?? [],
                    },
                },
            }
        );

    private static PackInstallationRequest CreateRequest(
        IReadOnlyDictionary<string, string>? parameters = null,
        IReadOnlySet<string>? skippedVariables = null
    ) =>
        new(new PackReference("root", null), null, false)
        {
            Parameters = parameters ?? new Dictionary<string, string>(StringComparer.Ordinal),
            SkippedVariables = skippedVariables ?? new HashSet<string>(StringComparer.Ordinal),
        };
}
