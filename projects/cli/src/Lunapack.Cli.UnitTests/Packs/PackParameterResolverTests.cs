using Lunapack.Cli.Catalog;
using Lunapack.Cli.Packs;
using Lunapack.Cli.Packs.Manifest;
using Lunapack.Cli.Packs.Planning;
using Lunapack.Cli.Project;

namespace Lunapack.Cli.UnitTests.Packs;

[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Design",
    "MA0002:Use an overload that has a IEqualityComparer<string> or IComparer<string> parameter",
    Justification = "TUnit generic assertions expose no comparer overload for these values; retained under the warning policy established by ADR-0006."
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
    public async Task FindPromptable_WhenOptionalParameterHasDefault_ReturnsPromptMetadata()
    {
        var pack = CreatePack("ci", "bool");
        pack.Manifest.Parameters["companyName"].Default = false;

        var result = PackParameterResolver.FindPromptable(
            new ResolvedPackGraph([pack]),
            new ProjectConfiguration(),
            CreateRequest(),
            includeOptional: true
        );

        await Assert.That(result.IsSuccess).IsTrue();
        await Assert.That(result.RequireValue().Single().Id).IsEqualTo("companyName");
        await Assert.That(result.RequireValue().Single().Definition.Default is false).IsTrue();
    }

    [Test]
    public async Task Prompt_WhenRequiredWhenIsTrue_PromptsInDefinitionOrder()
    {
        var pack = CreatePackWithoutParameters("root");
        pack.Manifest.Parameters["includeApi"] = new PackManifest.PackParameter
        {
            Type = "bool",
            Default = false,
        };
        pack.Manifest.Parameters["apiName"] = new PackManifest.PackParameter
        {
            Type = "string",
            RequiredWhen = "includeApi",
        };
        var prompted = new List<string>();

        var result = PackParameterResolver.Prompt(
            new ResolvedPackGraph([pack]),
            new ProjectConfiguration(),
            CreateRequest(),
            includeOptional: true,
            prompts =>
            {
                var prompt = prompts.Single();
                prompted.Add(prompt.Id);
                return new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
                {
                    [prompt.Id] = string.Equals(prompt.Id, "includeApi", StringComparison.Ordinal)
                        ? ["true"]
                        : ["api"],
                };
            }
        );

        await Assert.That(result.IsSuccess).IsTrue();
        await Assert.That(prompted).IsEquivalentTo(["includeApi", "apiName"]);
    }

    [Test]
    public async Task FindUnresolvedRequired_WhenRequiredWhenIsTrue_ReturnsConditionalPrompt()
    {
        var pack = CreatePackWithoutParameters("root");
        pack.Manifest.Parameters["includeApi"] = new PackManifest.PackParameter
        {
            Type = "bool",
            Default = true,
        };
        pack.Manifest.Parameters["apiName"] = new PackManifest.PackParameter
        {
            Type = "string",
            RequiredWhen = "includeApi",
        };

        var result = PackParameterResolver.FindUnresolvedRequired(
            new ResolvedPackGraph([pack]),
            new ProjectConfiguration(),
            CreateRequest()
        );

        await Assert.That(result.IsSuccess).IsTrue();
        await Assert.That(result.RequireValue().Single().Id).IsEqualTo("apiName");
    }

    [Test]
    public async Task Resolve_WhenRequiredWhenIsFalse_UsesOptionalValue()
    {
        var pack = CreatePackWithoutParameters("root");
        pack.Manifest.Parameters["includeApi"] = new PackManifest.PackParameter
        {
            Type = "bool",
            Default = false,
        };
        pack.Manifest.Parameters["apiName"] = new PackManifest.PackParameter
        {
            Type = "string",
            RequiredWhen = "includeApi",
        };

        var result = PackParameterResolver.Resolve(
            new ResolvedPackGraph([pack]),
            new ProjectConfiguration(),
            CreateRequest()
        );

        await Assert.That(result.IsSuccess).IsTrue();
        await Assert.That(result.RequireValue().Values["apiName"].StringValue).IsEmpty();
    }

    [Test]
    public async Task Prompt_WhenReferenceBranchIsInactive_SkipsItsParameters()
    {
        var dependency = CreatePack("dependency", "string");
        var root = CreatePackWithoutParameters("root");
        root.Manifest.Parameters["includeDependency"] = new PackManifest.PackParameter
        {
            Type = "bool",
            Default = true,
        };
        root.Manifest.Packs.Add(
            new PackManifest.PackReference
            {
                Id = "dependency",
                Version = "1.0.0",
                Condition = "includeDependency",
            }
        );
        var prompted = new List<string>();

        var result = PackParameterResolver.Prompt(
            new ResolvedPackGraph(
                [dependency, root],
                new HashSet<string>(["root"], StringComparer.Ordinal)
            ),
            new ProjectConfiguration(),
            CreateRequest(),
            includeOptional: true,
            prompts =>
            {
                var prompt = prompts.Single();
                prompted.Add(prompt.Id);
                return new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
                {
                    [prompt.Id] = ["false"],
                };
            }
        );

        await Assert.That(result.IsSuccess).IsTrue();
        await Assert.That(prompted).IsEquivalentTo(["includeDependency"]);
    }

    [Test]
    public async Task Prompt_WhenDependencyHasAnotherActivePath_IncludesItsParameters()
    {
        var shared = CreatePack("shared", "string");
        var bridge = CreatePackWithoutParameters("bridge");
        bridge.Manifest.Packs.Add(
            new PackManifest.PackReference { Id = "shared", Version = "1.0.0" }
        );
        var root = CreatePackWithoutParameters("root");
        root.Manifest.Parameters["includeDirect"] = new PackManifest.PackParameter
        {
            Type = "bool",
            Default = true,
        };
        root.Manifest.Packs =
        [
            new PackManifest.PackReference
            {
                Id = "shared",
                Version = "1.0.0",
                Condition = "includeDirect",
            },
            new PackManifest.PackReference { Id = "bridge", Version = "1.0.0" },
        ];
        var prompted = new List<string>();

        var result = PackParameterResolver.Prompt(
            new ResolvedPackGraph(
                [shared, bridge, root],
                new HashSet<string>(["root"], StringComparer.Ordinal)
            ),
            new ProjectConfiguration(),
            CreateRequest(),
            includeOptional: true,
            prompts =>
            {
                var prompt = prompts.Single();
                prompted.Add(prompt.Id);
                return new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
                {
                    [prompt.Id] = string.Equals(
                        prompt.Id,
                        "includeDirect",
                        StringComparison.Ordinal
                    )
                        ? ["false"]
                        : ["value"],
                };
            }
        );

        await Assert.That(result.IsSuccess).IsTrue();
        await Assert.That(prompted).IsEquivalentTo(["includeDirect", "companyName"]);
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
    public async Task Resolve_WhenMultiSelectValuesProvided_PreservesInputOrder()
    {
        var pack = CreatePack("features", "enum", values: ["api", "docker"]);
        pack.Manifest.Parameters["companyName"].Multiple = true;
        var result = PackParameterResolver.Resolve(
            new ResolvedPackGraph([pack]),
            new ProjectConfiguration(),
            CreateRequest(
                parameterValues: new Dictionary<string, IReadOnlyList<string>>
                {
                    ["companyName"] = ["docker", "api"],
                }
            )
        );

        await Assert.That(result.IsSuccess).IsTrue();
        await Assert
            .That(result.RequireValue().Values["companyName"].StringValues)
            .IsEquivalentTo(["docker", "api"]);
    }

    [Test]
    public async Task Resolve_WhenMultiSelectValueRepeated_ReturnsFailure()
    {
        var pack = CreatePack("features", "enum", values: ["api"]);
        pack.Manifest.Parameters["companyName"].Multiple = true;
        var result = PackParameterResolver.Resolve(
            new ResolvedPackGraph([pack]),
            new ProjectConfiguration(),
            CreateRequest(
                parameterValues: new Dictionary<string, IReadOnlyList<string>>
                {
                    ["companyName"] = ["api", "api"],
                }
            )
        );

        await Assert.That(result.IsSuccess).IsFalse();
    }

    [Test]
    public async Task Resolve_WhenScalarParameterRepeated_ReturnsFailure()
    {
        var result = PackParameterResolver.Resolve(
            new ResolvedPackGraph([CreatePack("name", "string")]),
            new ProjectConfiguration(),
            CreateRequest(
                parameterValues: new Dictionary<string, IReadOnlyList<string>>
                {
                    ["companyName"] = ["Lunaris", "Digital"],
                }
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
    public async Task Resolve_WhenSharedEnumsDifferInMultipleShape_ReturnsFailure()
    {
        var root = CreatePack("root", "enum", values: ["api"]);
        root.Manifest.Parameters["companyName"].Multiple = true;
        var dependency = CreatePack("dependency", "enum", values: ["api"]);

        var result = PackParameterResolver.Resolve(
            new ResolvedPackGraph([root, dependency]),
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
    public async Task Resolve_WhenCompositeSetsTransientMultiSelect_BindsArray()
    {
        var dependency = CreatePack("dependency", "enum", required: true, values: ["api"]);
        dependency.Manifest.Parameters["companyName"].Multiple = true;
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
                        Parameters = new Dictionary<string, object>
                        {
                            ["companyName"] = new List<object> { "api" },
                        },
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
            .That(result.RequireValue().Values["companyName"].StringValues)
            .IsEquivalentTo(["api"]);
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

    [Test]
    public async Task Resolve_WhenOptionalMultiSelectOmitted_UsesEmptyArray()
    {
        var pack = CreatePack("features", "enum", values: ["api"]);
        pack.Manifest.Parameters["companyName"].Multiple = true;

        var result = PackParameterResolver.Resolve(
            new ResolvedPackGraph([pack]),
            new ProjectConfiguration(),
            CreateRequest()
        );

        await Assert.That(result.IsSuccess).IsTrue();
        await Assert.That(result.RequireValue().Values["companyName"].StringValues).IsEmpty();
    }

    [Test]
    public async Task Resolve_WhenProjectVariableProvidesMultiSelect_BindsArray()
    {
        var pack = CreatePack("features", "enum", required: true, values: ["api", "docker"]);
        pack.Manifest.Parameters["companyName"].Multiple = true;

        var result = PackParameterResolver.Resolve(
            new ResolvedPackGraph([pack]),
            new ProjectConfiguration
            {
                Variables = new Dictionary<string, object>
                {
                    ["companyName"] = new List<object> { "api", "docker" },
                },
            },
            CreateRequest()
        );

        await Assert.That(result.IsSuccess).IsTrue();
        await Assert
            .That(result.RequireValue().Values["companyName"].StringValues)
            .IsEquivalentTo(["api", "docker"]);
    }

    [Test]
    public async Task Resolve_WhenOptionalParameterHasDefault_UsesTypedDefault()
    {
        var pack = CreatePack("defaults", "string");
        pack.Manifest.Parameters["companyName"].Default = "Lunaris";

        var result = PackParameterResolver.Resolve(
            new ResolvedPackGraph([pack]),
            new ProjectConfiguration(),
            CreateRequest()
        );

        await Assert.That(result.IsSuccess).IsTrue();
        await Assert
            .That(result.RequireValue().Values["companyName"].StringValue)
            .IsEqualTo("Lunaris");
    }

    [Test]
    public async Task Resolve_WhenMultiSelectHasDefault_UsesOrderedArray()
    {
        var pack = CreatePack("defaults", "enum", values: ["api", "docker"]);
        pack.Manifest.Parameters["companyName"].Multiple = true;
        pack.Manifest.Parameters["companyName"].Default = new List<object> { "docker", "api" };

        var result = PackParameterResolver.Resolve(
            new ResolvedPackGraph([pack]),
            new ProjectConfiguration(),
            CreateRequest()
        );

        await Assert.That(result.IsSuccess).IsTrue();
        await Assert
            .That(result.RequireValue().Values["companyName"].StringValues)
            .IsEquivalentTo(["docker", "api"]);
    }

    [Test]
    public async Task Resolve_WhenMultiSelectProjectVariableContainsUnknownValue_ReturnsFailure()
    {
        var pack = CreatePack("features", "enum", values: ["api"]);
        pack.Manifest.Parameters["companyName"].Multiple = true;

        var result = PackParameterResolver.Resolve(
            new ResolvedPackGraph([pack]),
            new ProjectConfiguration
            {
                Variables = new Dictionary<string, object>
                {
                    ["companyName"] = new List<object> { "docker" },
                },
            },
            CreateRequest()
        );

        await Assert.That(result.IsSuccess).IsFalse();
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

    private static DiscoveredPack CreatePackWithoutParameters(string packId) =>
        new(
            "source",
            packId,
            new PackManifest
            {
                Id = packId,
                Version = "1.0.0",
                Author = "Example Author",
                License = "MIT",
            }
        );

    private static PackInstallationRequest CreateRequest(
        IReadOnlyDictionary<string, string>? parameters = null,
        IReadOnlyDictionary<string, IReadOnlyList<string>>? parameterValues = null,
        IReadOnlySet<string>? skippedVariables = null
    ) =>
        new(new PackReference("root", null), null, false)
        {
            Parameters = parameters ?? new Dictionary<string, string>(StringComparer.Ordinal),
            ParameterValues =
                parameterValues
                ?? new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal),
            SkippedVariables = skippedVariables ?? new HashSet<string>(StringComparer.Ordinal),
        };
}
