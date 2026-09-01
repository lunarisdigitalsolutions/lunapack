using System.Text.Json;
using System.Text.RegularExpressions;
using Lunapack.Cli.Application.Paths;
using Lunapack.Cli.Packs.Manifest;
using Lunapack.Cli.Project;
using Lunapack.Cli.Sources;
using Lunapack.Cli.Trust;

namespace Lunapack.Cli.UnitTests.Application.Serialization;

public sealed class ManifestSchemaTests
{
    [Test]
    public async Task PackSchema_WhenManagedFileTargetDeclared_RequiresSafeRelativePath()
    {
        using var schema = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestData", "pack.schema.json"))
        );
        var target = schema
            .RootElement.GetProperty("definitions")
            .GetProperty("managedFile")
            .GetProperty("properties")
            .GetProperty("target")
            .GetProperty("$ref")
            .GetString();

        await Assert.That(target).IsEqualTo("#/definitions/sourceRelativePath");
    }

    [Test]
    public async Task LockSchema_WhenEffectiveTargetDeclared_RequiresSafeRelativePath()
    {
        using var schema = JsonDocument.Parse(
            File.ReadAllText(
                Path.Combine(AppContext.BaseDirectory, "TestData", "lunapack-lock.schema.json")
            )
        );
        var definitions = schema.RootElement.GetProperty("definitions");

        await Assert
            .That(
                definitions
                    .GetProperty("managedFile")
                    .GetProperty("properties")
                    .GetProperty("targetPath")
                    .GetProperty("$ref")
                    .GetString()
            )
            .IsEqualTo("#/definitions/repositoryRelativePath");
        await Assert
            .That(
                definitions
                    .GetProperty("linkFile")
                    .GetProperty("properties")
                    .GetProperty("targetPath")
                    .GetProperty("$ref")
                    .GetString()
            )
            .IsEqualTo("#/definitions/repositoryRelativePath");
    }

    [Test]
    public async Task ProjectSchema_WhenRequestedPackDeclared_AllowsPackRemapping()
    {
        using var schema = JsonDocument.Parse(
            File.ReadAllText(
                Path.Combine(AppContext.BaseDirectory, "TestData", "lunapack.schema.json")
            )
        );
        var remap = schema
            .RootElement.GetProperty("definitions")
            .GetProperty("requestedPack")
            .GetProperty("properties")
            .GetProperty("remap")
            .GetProperty("$ref")
            .GetString();

        await Assert.That(remap).IsEqualTo("#/definitions/remapping");
    }

    [Test]
    public async Task ProjectSchema_WhenTrustDeclared_AllowsDenialWithoutGrantCollections()
    {
        using var schema = JsonDocument.Parse(
            File.ReadAllText(
                Path.Combine(AppContext.BaseDirectory, "TestData", "lunapack.schema.json")
            )
        );
        var definitions = schema.RootElement.GetProperty("definitions");
        var projectTrust = definitions.GetProperty("projectTrust");
        var denial = definitions.GetProperty("scriptDenial");

        await Assert.That(projectTrust.TryGetProperty("required", out _)).IsFalse();
        await Assert
            .That(projectTrust.GetProperty("properties").TryGetProperty("deny", out _))
            .IsTrue();
        await Assert
            .That(
                denial
                    .GetProperty("properties")
                    .GetProperty("scripts")
                    .GetProperty("default")
                    .GetBoolean()
            )
            .IsFalse();
    }

    [Test]
    public async Task UserSettingsSchema_WhenTrustDeclared_SeparatesDenialFromAcknowledgements()
    {
        using var schema = JsonDocument.Parse(
            File.ReadAllText(
                Path.Combine(
                    AppContext.BaseDirectory,
                    "TestData",
                    "lunapack-user-settings.schema.json"
                )
            )
        );
        var definitions = schema.RootElement.GetProperty("definitions");
        var userTrust = definitions.GetProperty("userTrust");
        var localTrust = definitions.GetProperty("localProjectTrust");
        var acknowledgements = definitions.GetProperty("trustAcknowledgements");

        await Assert.That(userTrust.TryGetProperty("required", out _)).IsFalse();
        await Assert.That(localTrust.TryGetProperty("required", out _)).IsFalse();
        await Assert
            .That(userTrust.GetProperty("properties").TryGetProperty("deny", out _))
            .IsTrue();
        await Assert
            .That(localTrust.GetProperty("properties").TryGetProperty("deny", out _))
            .IsTrue();
        await Assert
            .That(acknowledgements.GetProperty("properties").TryGetProperty("deny", out _))
            .IsFalse();
    }

    [Test]
    public async Task PackManifest_WhenRequiredMetadataMissing_IsRejected()
    {
        var manifest = new PackManifest { Id = "example", Version = "1.0.0" };

        var issues = ManifestModelValidator.Validate(manifest);

        await Assert.That(issues).Contains("Pack author is required.");
        await Assert.That(issues).Contains("Pack license is required.");
    }

    [Test]
    public async Task PackManifest_WhenOptionalMetadataEmpty_IsRejected()
    {
        var manifest = new PackManifest
        {
            Id = "example",
            Version = "1.0.0",
            Author = string.Empty,
            License = "MIT",
        };

        var issues = ManifestModelValidator.Validate(manifest);

        await Assert.That(issues).Contains("Pack author cannot be empty.");
    }

    [Test]
    public async Task PackManifest_WhenParameterDefaultMatchesType_IsAccepted()
    {
        var manifest = new PackManifest
        {
            Id = "example",
            Version = "1.0.0",
            Author = "Example Author",
            License = "MIT",
            Parameters = new Dictionary<string, PackManifest.PackParameter>(StringComparer.Ordinal)
            {
                ["includeCi"] = new() { Type = "bool", Default = true },
            },
        };

        var issues = ManifestModelValidator.Validate(manifest);

        await Assert.That(issues).IsEmpty();
    }

    [Test]
    public async Task PackManifest_WhenEnumDefaultIsNotAllowed_IsRejected()
    {
        var manifest = new PackManifest
        {
            Id = "example",
            Version = "1.0.0",
            Author = "Example Author",
            License = "MIT",
            Parameters = new Dictionary<string, PackManifest.PackParameter>(StringComparer.Ordinal)
            {
                ["license"] = new()
                {
                    Type = "enum",
                    Values = ["mit"],
                    Default = "apache-2.0",
                },
            },
        };

        var issues = ManifestModelValidator.Validate(manifest);

        await Assert
            .That(issues)
            .Contains("Enum parameter 'license' default must be one of its values.");
    }

    [Test]
    public async Task PackManifest_WhenMultiSelectEnumDefaultIsAllowed_IsAccepted()
    {
        var manifest = CreateValidPackManifest();
        manifest.Parameters["features"] = new PackManifest.PackParameter
        {
            Type = "enum",
            Multiple = true,
            Values = ["api", "docker"],
            Default = new List<object> { "api", "docker" },
        };

        var issues = ManifestModelValidator.Validate(manifest);

        await Assert.That(issues).IsEmpty();
    }

    [Test]
    public async Task PackManifest_WhenMultiSelectEnumDefaultContainsUnknownValue_IsRejected()
    {
        var manifest = CreateValidPackManifest();
        manifest.Parameters["features"] = new PackManifest.PackParameter
        {
            Type = "enum",
            Multiple = true,
            Values = ["api"],
            Default = new List<object> { "docker" },
        };

        var issues = ManifestModelValidator.Validate(manifest);

        await Assert
            .That(issues)
            .Contains("Enum parameter 'features' defaults must be among its values.");
    }

    [Test]
    public async Task PackManifest_WhenMultiSelectEnumDefaultContainsDuplicate_IsRejected()
    {
        var manifest = CreateValidPackManifest();
        manifest.Parameters["features"] = new PackManifest.PackParameter
        {
            Type = "enum",
            Multiple = true,
            Values = ["api"],
            Default = new List<object> { "api", "api" },
        };

        var issues = ManifestModelValidator.Validate(manifest);

        await Assert
            .That(issues)
            .Contains("Parameter 'features' has a default value incompatible with its type.");
    }

    [Test]
    public async Task PackManifest_WhenMultipleUsedForString_IsRejected()
    {
        var manifest = CreateValidPackManifest();
        manifest.Parameters["features"] = new PackManifest.PackParameter
        {
            Type = "string",
            Multiple = true,
        };

        var issues = ManifestModelValidator.Validate(manifest);

        await Assert
            .That(issues)
            .Contains("Parameter 'features' can only set multiple for enum values.");
    }

    [Test]
    [Arguments("example-pack", true)]
    [Arguments("Example-Pack2", true)]
    [Arguments("example_pack", false)]
    [Arguments("-example", false)]
    [Arguments("example-", false)]
    [Arguments("example--pack", false)]
    public async Task PackManifest_WhenIdProvided_RequiresKebabCase(string id, bool expectedValid)
    {
        var manifest = new PackManifest
        {
            Id = id,
            Version = "1.0.0",
            Author = "Example Author",
            License = "MIT",
        };

        var issues = ManifestModelValidator.Validate(manifest);

        await Assert.That(issues.Count == 0).IsEqualTo(expectedValid);
    }

    [Test]
    public async Task PackManifest_WhenCompositeReferenceIdIsNotKebabCase_IsRejected()
    {
        var manifest = new PackManifest
        {
            Id = "example",
            Version = "1.0.0",
            Author = "Example Author",
            License = "MIT",
            Packs = [new PackManifest.PackReference { Id = "example_pack", Version = "1.0.0" }],
        };

        var issues = ManifestModelValidator.Validate(manifest);

        await Assert
            .That(issues)
            .Contains(
                "Pack reference ID 'example_pack' must use hyphen-separated alphanumeric segments."
            );
    }

    [Test]
    public async Task PackSchema_WhenIdDeclared_RequiresKebabCase()
    {
        using var schema = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestData", "pack.schema.json"))
        );
        var pattern =
            schema
                .RootElement.GetProperty("properties")
                .GetProperty("id")
                .GetProperty("pattern")
                .GetString()
            ?? throw new InvalidOperationException("Pack ID schema pattern is missing.");
        await Assert
            .That(
                Regex.IsMatch(
                    "example-pack",
                    pattern,
                    RegexOptions.CultureInvariant,
                    TimeSpan.FromSeconds(1)
                )
            )
            .IsTrue();
        await Assert
            .That(
                Regex.IsMatch(
                    "example_pack",
                    pattern,
                    RegexOptions.CultureInvariant,
                    TimeSpan.FromSeconds(1)
                )
            )
            .IsFalse();
    }

    [Test]
    [Arguments("https://lunapack.dev/packs/example", true)]
    [Arguments("HTTPS://lunapack.dev/packs/example", true)]
    [Arguments("http://", false)]
    [Arguments("https://exa mple.test", false)]
    [Arguments("relative/home", false)]
    [Arguments("ftp://lunapack.dev/example", false)]
    public async Task PackManifest_WhenHomepageProvided_ValidatesAbsoluteWebUri(
        string homepage,
        bool expectedValid
    )
    {
        var manifest = new PackManifest
        {
            Id = "example",
            Version = "1.0.0",
            Author = "Example Author",
            License = "MIT",
            Homepage = homepage,
        };

        var issues = ManifestModelValidator.Validate(manifest);

        await Assert.That(issues.Count == 0).IsEqualTo(expectedValid);
    }

    [Test]
    [Arguments("https://lunapack.dev/packs/example", true)]
    [Arguments("HTTPS://lunapack.dev/packs/example", true)]
    [Arguments("http://", false)]
    [Arguments("https://exa mple.test", false)]
    [Arguments("ftp://lunapack.dev/example", false)]
    public async Task PackSchema_WhenHomepageProvided_MatchesRuntimeUriBoundary(
        string homepage,
        bool expectedValid
    )
    {
        using var schema = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestData", "pack.schema.json"))
        );
        var pattern =
            schema
                .RootElement.GetProperty("properties")
                .GetProperty("homepage")
                .GetProperty("pattern")
                .GetString()
            ?? throw new InvalidOperationException("Pack homepage schema pattern is missing.");
        await Assert
            .That(
                Regex.IsMatch(
                    homepage,
                    pattern,
                    RegexOptions.CultureInvariant,
                    TimeSpan.FromSeconds(1)
                )
            )
            .IsEqualTo(expectedValid);
    }

    [Test]
    public async Task PackSchema_WhenRequiredMetadataDeclared_RequiresAuthorAndLicense()
    {
        using var schema = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestData", "pack.schema.json"))
        );

        var requiredProperties = schema
            .RootElement.GetProperty("required")
            .EnumerateArray()
            .Select(property => property.GetString())
            .ToArray();

        await Assert.That(requiredProperties).Contains("author");
        await Assert.That(requiredProperties).Contains("license");
    }

    [Test]
    public async Task PackSchema_WhenDraftDeclared_DefinesOptionalFalseDefault()
    {
        using var schema = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestData", "pack.schema.json"))
        );

        var draft = schema.RootElement.GetProperty("properties").GetProperty("draft");
        var requiredProperties = schema
            .RootElement.GetProperty("required")
            .EnumerateArray()
            .Select(property => property.GetString())
            .ToArray();

        await Assert.That(draft.GetProperty("type").GetString()).IsEqualTo("boolean");
        await Assert.That(draft.GetProperty("default").GetBoolean()).IsFalse();
        await Assert.That(requiredProperties).DoesNotContain("draft");
    }

    [Test]
    public async Task PackSchema_WhenHooksDeclared_DefinesOrderedTypedUnionAndRejectsLegacyProperty()
    {
        using var schema = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestData", "pack.schema.json"))
        );
        var root = schema.RootElement;
        var properties = root.GetProperty("properties");
        var definitions = root.GetProperty("definitions");
        var hookVariants = definitions
            .GetProperty("lifecycleHook")
            .GetProperty("oneOf")
            .EnumerateArray()
            .Select(variant => variant.GetProperty("$ref").GetString())
            .ToArray();

        await Assert.That(properties.TryGetProperty("hooks", out _)).IsTrue();
        await Assert.That(properties.TryGetProperty("scripts", out _)).IsFalse();
        await Assert
            .That(definitions.GetProperty("lifecycleHookList").GetProperty("minItems").GetInt32())
            .IsEqualTo(1);
        await Assert
            .That(string.Join(",", hookVariants))
            .IsEqualTo("#/definitions/scriptHook,#/definitions/instructionHook");
        await Assert
            .That(
                definitions
                    .GetProperty("scriptHook")
                    .GetProperty("properties")
                    .TryGetProperty("condition", out _)
            )
            .IsTrue();
        await Assert
            .That(
                definitions
                    .GetProperty("instructionHook")
                    .GetProperty("properties")
                    .TryGetProperty("condition", out _)
            )
            .IsTrue();
    }

    [Test]
    public async Task PackSchema_WhenMultiSelectDeclared_DefinesArrayContracts()
    {
        using var schema = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestData", "pack.schema.json"))
        );
        var definitions = schema.RootElement.GetProperty("definitions");
        var parameter = definitions.GetProperty("parameter");
        var parameterVariants = parameter.GetProperty("oneOf").EnumerateArray().ToArray();

        await Assert
            .That(parameter.GetProperty("properties").TryGetProperty("multiple", out _))
            .IsTrue();
        await Assert.That(parameterVariants).Count().IsEqualTo(4);
        await Assert
            .That(definitions.GetProperty("stringArray").GetProperty("uniqueItems").GetBoolean())
            .IsTrue();
    }

    [Test]
    public async Task PackManifest_WhenManagedFileStrategyInvalid_IsRejected()
    {
        var manifest = CreateValidPackManifest();
        manifest.ManagedFiles[0].Strategy = new PackManifest.PackManagedFileStrategy
        {
            Type = "copy",
            Method = "lines",
        };

        var issues = ManifestModelValidator.Validate(manifest);

        await Assert.That(issues).IsNotEmpty();
    }

    [Test]
    public async Task PackManifest_WhenEnumValuesDuplicated_IsRejected()
    {
        var manifest = CreateValidPackManifest();
        manifest.Parameters["environment"] = new PackManifest.PackParameter
        {
            Type = "enum",
            Values = ["development", "development"],
        };

        var issues = ManifestModelValidator.Validate(manifest);

        await Assert.That(issues).IsNotEmpty();
    }

    [Test]
    public async Task PackManifest_WhenHooksOmitted_IsAccepted()
    {
        var manifest = CreateValidPackManifest();

        var issues = ManifestModelValidator.Validate(manifest);

        await Assert.That(issues).IsEmpty();
    }

    [Test]
    public async Task PackManifest_WhenMixedHooksValid_PreservesDeclarationOrder()
    {
        var manifest = CreateValidPackManifest();
        manifest.Hooks = new PackManifest.PackHooks
        {
            PreInstall =
            [
                new PackManifest.PackHook { Type = "instruction", File = "instructions/setup.md" },
                new PackManifest.PackHook
                {
                    Type = "script",
                    Command = "dotnet",
                    Arguments = ["tool", "restore"],
                },
                new PackManifest.PackHook
                {
                    Type = "script",
                    File = "scripts/setup.ps1",
                    Runner = "pwsh",
                },
            ],
        };

        var issues = ManifestModelValidator.Validate(manifest);

        await Assert.That(issues).IsEmpty();
        await Assert
            .That(string.Join(",", manifest.Hooks.PreInstall.Select(hook => hook.Type)))
            .IsEqualTo("instruction,script,script");
    }

    [Test]
    public async Task PackManifest_WhenInstructionOnly_IsAccepted()
    {
        var manifest = CreateValidPackManifest();
        manifest.ManagedFiles = [];
        manifest.Hooks = new PackManifest.PackHooks
        {
            PostInstall =
            [
                new PackManifest.PackHook
                {
                    Type = "instruction",
                    File = "instructions/setup.md",
                    Templating = true,
                },
            ],
        };

        var issues = ManifestModelValidator.Validate(manifest);

        await Assert.That(issues).IsEmpty();
    }

    [Test]
    public async Task PackManifest_WhenHookEventEmpty_IsRejected()
    {
        var manifest = CreateValidPackManifest();
        manifest.Hooks = new PackManifest.PackHooks { PreInstall = [] };

        var issues = ManifestModelValidator.Validate(manifest);

        await Assert.That(issues).IsNotEmpty();
    }

    [Test]
    [Arguments("script", "scripts/setup.ps1", "pwsh", "dotnet")]
    [Arguments("script", "scripts/setup.ps1", null, null)]
    [Arguments("instruction", "instructions/setup.txt", null, null)]
    [Arguments("instruction", "../instructions/setup.md", null, null)]
    [Arguments("unknown", "instructions/setup.md", null, null)]
    public async Task PackManifest_WhenHookMalformed_IsRejected(
        string type,
        string file,
        string? runner,
        string? command
    )
    {
        var manifest = CreateValidPackManifest();
        manifest.Hooks = new PackManifest.PackHooks
        {
            PreInstall =
            [
                new PackManifest.PackHook
                {
                    Type = type,
                    File = file,
                    Runner = runner,
                    Command = command,
                },
            ],
        };

        var issues = ManifestModelValidator.Validate(manifest);

        await Assert.That(issues).IsNotEmpty();
    }

    [Test]
    public async Task PackManifest_WhenTypeSpecificPropertiesMixed_IsRejected()
    {
        var manifest = CreateValidPackManifest();
        manifest.Hooks = new PackManifest.PackHooks
        {
            PreInstall =
            [
                new PackManifest.PackHook
                {
                    Type = "instruction",
                    File = "instructions/setup.md",
                    Command = "dotnet",
                },
                new PackManifest.PackHook
                {
                    Type = "script",
                    Command = "dotnet",
                    Templating = false,
                },
            ],
        };

        var issues = ManifestModelValidator.Validate(manifest);

        await Assert.That(issues).Count().IsEqualTo(2);
    }

    [Test]
    public async Task PackManifest_WhenHookFilesUseWindowsSeparators_NormalizesBothTypes()
    {
        var manifest = CreateValidPackManifest();
        manifest.Hooks = new PackManifest.PackHooks
        {
            PreInstall =
            [
                new PackManifest.PackHook
                {
                    Type = "script",
                    File = @"scripts\setup.ps1",
                    Runner = "pwsh",
                },
                new PackManifest.PackHook { Type = "instruction", File = @"instructions\setup.md" },
            ],
        };

        var normalized = PackManifestPathNormalizer.Normalize(manifest);

        await Assert
            .That(
                normalized
                    .Hooks.RequireNotNull()
                    .PreInstall.RequireNotNull()
                    .Select(hook => hook.File ?? throw new InvalidOperationException())
            )
            .IsEquivalentTo(["scripts/setup.ps1", "instructions/setup.md"]);
    }

    [Test]
    public async Task PackManifest_WhenReferenceSuppressionValid_IsAccepted()
    {
        var manifest = CreateValidPackManifest();
        manifest.Packs =
        [
            new PackManifest.PackReference
            {
                Id = "dependency",
                Version = "1.0.0",
                DisabledHooks = ["preInstall", "postUpdate"],
            },
        ];

        var issues = ManifestModelValidator.Validate(manifest);

        await Assert.That(issues).IsEmpty();
    }

    [Test]
    public async Task PackManifest_WhenReferenceParameterIsUniqueStringArray_IsAccepted()
    {
        var manifest = CreateValidPackManifest();
        manifest.Packs =
        [
            new PackManifest.PackReference
            {
                Id = "dependency",
                Version = "1.0.0",
                Parameters = new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    ["features"] = new List<object> { "api", "docker" },
                },
            },
        ];

        var issues = ManifestModelValidator.Validate(manifest);

        await Assert.That(issues).IsEmpty();
    }

    [Test]
    public async Task PackManifest_WhenReferenceSuppressionDuplicated_IsRejected()
    {
        var manifest = CreateValidPackManifest();
        manifest.Packs =
        [
            new PackManifest.PackReference
            {
                Id = "dependency",
                Version = "1.0.0",
                DisabledHooks = ["preInstall", "preInstall"],
            },
        ];

        var issues = ManifestModelValidator.Validate(manifest);

        await Assert.That(issues).IsNotEmpty();
    }

    [Test]
    public async Task PackManifest_WhenReferenceSuppressionUnknown_IsRejected()
    {
        var manifest = CreateValidPackManifest();
        manifest.Packs =
        [
            new PackManifest.PackReference
            {
                Id = "dependency",
                Version = "1.0.0",
                DisabledHooks = ["preRemove"],
            },
        ];

        var issues = ManifestModelValidator.Validate(manifest);

        await Assert.That(issues).IsNotEmpty();
    }

    [Test]
    public async Task ProjectConfiguration_WhenLocalSourceAbsolute_IsRejected()
    {
        var configuration = new ProjectConfiguration
        {
            SchemaVersion = 1,
            Sources = [new ProjectConfiguration.LocalSource { Name = "local", Path = @"C:\packs" }],
        };

        var issues = ManifestModelValidator.Validate(configuration);

        await Assert.That(issues).IsNotEmpty();
    }

    [Test]
    public async Task ProjectConfiguration_WhenGitSourcePathUnsafe_IsRejected()
    {
        var configuration = new ProjectConfiguration
        {
            SchemaVersion = 1,
            Sources =
            [
                new ProjectConfiguration.GitSource
                {
                    Name = "git",
                    Url = "https://example.test/packs.git",
                    Path = "../packs",
                },
            ],
        };

        var issues = ManifestModelValidator.Validate(configuration);

        await Assert.That(issues).IsNotEmpty();
    }

    [Test]
    public async Task ProjectConfiguration_WhenSourceAndRequestedPackValid_IsAccepted()
    {
        var configuration = new ProjectConfiguration
        {
            SchemaVersion = 1,
            Sources = [new ProjectConfiguration.LocalSource { Name = "local", Path = "packs" }],
            Packs =
            [
                new ProjectConfiguration.RequestedPack
                {
                    Id = "example",
                    Version = "1.0.0",
                    Destination = "config",
                },
            ],
            Variables = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["enableFeature"] = true,
            },
        };

        var issues = ManifestModelValidator.Validate(configuration);

        await Assert.That(issues).IsEmpty();
    }

    [Test]
    public async Task ProjectConfiguration_WhenVariableIsUniqueStringArray_IsAccepted()
    {
        var configuration = new ProjectConfiguration
        {
            SchemaVersion = 1,
            Variables = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["features"] = new List<object> { "api", "docker" },
            },
        };

        var issues = ManifestModelValidator.Validate(configuration);

        await Assert.That(issues).IsEmpty();
    }

    [Test]
    public async Task ProjectConfiguration_WhenVariableArrayContainsDuplicate_IsRejected()
    {
        var configuration = new ProjectConfiguration
        {
            SchemaVersion = 1,
            Variables = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["features"] = new List<object> { "api", "api" },
            },
        };

        var issues = ManifestModelValidator.Validate(configuration);

        await Assert.That(issues).IsNotEmpty();
    }

    [Test]
    public async Task ProjectConfiguration_WhenSourceNamesDuplicated_IsRejected()
    {
        var configuration = new ProjectConfiguration
        {
            SchemaVersion = 1,
            Sources =
            [
                new ProjectConfiguration.LocalSource { Name = "shared", Path = "packs" },
                new ProjectConfiguration.GitSource
                {
                    Name = "shared",
                    Url = "https://example.test/packs.git",
                },
            ],
        };

        var issues = ManifestModelValidator.Validate(configuration);

        await Assert.That(issues).IsNotEmpty();
    }

    [Test]
    public async Task ProjectConfiguration_WhenProjectTrustValid_IsAccepted()
    {
        var configuration = CreateConfigurationWithSource();
        configuration.Trust = new ProjectConfiguration.ProjectTrust
        {
            Sources = ["local"],
            Packs = [new ProjectConfiguration.TrustedPack { Id = "example", Source = "local" }],
        };

        var issues = ManifestModelValidator.Validate(configuration);

        await Assert.That(issues).IsEmpty();
    }

    [Test]
    public async Task ProjectConfiguration_WhenProjectTrustDuplicated_IsRejected()
    {
        var configuration = CreateConfigurationWithSource();
        configuration.Trust = new ProjectConfiguration.ProjectTrust
        {
            Sources = ["local", "local"],
            Packs =
            [
                new ProjectConfiguration.TrustedPack { Id = "example", Source = "local" },
                new ProjectConfiguration.TrustedPack { Id = "example", Source = "local" },
            ],
        };

        var issues = ManifestModelValidator.Validate(configuration);

        await Assert.That(issues).IsNotEmpty();
    }

    [Test]
    public async Task ProjectConfiguration_WhenPackTrustUnboundOrVersioned_IsRejected()
    {
        var configuration = CreateConfigurationWithSource();
        configuration.Trust = new ProjectConfiguration.ProjectTrust
        {
            Packs =
            [
                new ProjectConfiguration.TrustedPack { Id = "example@1.0.0", Source = "unknown" },
            ],
        };

        var issues = ManifestModelValidator.Validate(configuration);

        await Assert.That(issues).IsNotEmpty();
    }

    [Test]
    public async Task ProjectLockFile_WhenGitCommitMissing_IsRejected()
    {
        var lockFile = new ProjectLockFile
        {
            SchemaVersion = 1,
            Packs =
            [
                new ProjectLockFile.ResolvedPack
                {
                    Id = "example",
                    Version = "1.0.0",
                    PackPath = "example",
                    GitSource = new GitSourceProvenance
                    {
                        Url = "https://example.test/packs.git",
                        ResolvedCommit = "not-a-commit",
                    },
                },
            ],
        };

        var issues = ManifestModelValidator.Validate(lockFile);

        await Assert.That(issues).IsNotEmpty();
    }

    [Test]
    public async Task ProjectLockFile_WhenManagedFileHashInvalid_IsRejected()
    {
        var lockFile = new ProjectLockFile
        {
            SchemaVersion = 1,
            Packs =
            [
                new ProjectLockFile.ResolvedPack
                {
                    Id = "example",
                    Version = "1.0.0",
                    SourcePath = "packs",
                    PackPath = "example",
                    ManagedFiles =
                    [
                        new ProjectLockFile.ManagedFile
                        {
                            TargetPath = "content.txt",
                            Sha256 = "invalid",
                        },
                    ],
                },
            ],
        };

        var issues = ManifestModelValidator.Validate(lockFile);

        await Assert.That(issues).IsNotEmpty();
    }

    [Test]
    public async Task ProjectLockFile_WhenLocalSourceIdentityValid_IsAccepted()
    {
        var lockFile = new ProjectLockFile
        {
            SchemaVersion = 1,
            Packs =
            [
                new ProjectLockFile.ResolvedPack
                {
                    Id = "example",
                    Version = "1.0.0",
                    SourceName = "local",
                    SourceIdentity = new ConfiguredSourceIdentity
                    {
                        Type = "local",
                        Path = "packs",
                    },
                    SourcePath = "packs",
                    PackPath = "example",
                },
            ],
        };

        var issues = ManifestModelValidator.Validate(lockFile);

        await Assert.That(issues).IsEmpty();
    }

    [Test]
    public async Task ProjectLockFile_WhenSourceIdentityMissing_IsRejected()
    {
        var lockFile = new ProjectLockFile
        {
            SchemaVersion = 1,
            Packs =
            [
                new ProjectLockFile.ResolvedPack
                {
                    Id = "example",
                    Version = "1.0.0",
                    SourcePath = "packs",
                    PackPath = "example",
                },
            ],
        };

        var issues = ManifestModelValidator.Validate(lockFile);

        await Assert.That(issues).IsNotEmpty();
    }

    [Test]
    public async Task ProjectLockFile_WhenGitIdentityAndProvenanceValid_IsAccepted()
    {
        var lockFile = new ProjectLockFile
        {
            SchemaVersion = 1,
            Packs =
            [
                new ProjectLockFile.ResolvedPack
                {
                    Id = "example",
                    Version = "1.0.0",
                    SourceName = "git",
                    SourceIdentity = new ConfiguredSourceIdentity
                    {
                        Type = "git",
                        Url = "https://example.test/packs.git",
                        Ref = "main",
                        Path = "packs",
                    },
                    GitSource = new GitSourceProvenance
                    {
                        Url = "https://example.test/packs.git",
                        Ref = "main",
                        Path = "packs",
                        ResolvedCommit = "0123456789abcdef0123456789abcdef01234567",
                    },
                    PackPath = "example",
                },
            ],
        };

        var issues = ManifestModelValidator.Validate(lockFile);

        await Assert.That(issues).IsEmpty();
    }

    [Test]
    public async Task ProjectLockFile_WhenGitIdentityDiffersFromProvenance_IsRejected()
    {
        var lockFile = new ProjectLockFile
        {
            SchemaVersion = 1,
            Packs =
            [
                new ProjectLockFile.ResolvedPack
                {
                    Id = "example",
                    Version = "1.0.0",
                    SourceName = "git",
                    SourceIdentity = new ConfiguredSourceIdentity
                    {
                        Type = "git",
                        Url = "https://other.example.test/packs.git",
                    },
                    GitSource = new GitSourceProvenance
                    {
                        Url = "https://example.test/packs.git",
                        ResolvedCommit = "0123456789abcdef0123456789abcdef01234567",
                    },
                    PackPath = "example",
                },
            ],
        };

        var issues = ManifestModelValidator.Validate(lockFile);

        await Assert.That(issues).IsNotEmpty();
    }

    [Test]
    public async Task UserSettings_WhenProjectPathRelative_IsRejected()
    {
        var settings = new UserSettings
        {
            Projects = new Dictionary<string, LocalProjectTrust>(StringComparer.Ordinal)
            {
                ["relative/project"] = new(),
            },
        };

        var issues = ManifestModelValidator.Validate(settings);

        await Assert.That(issues).IsNotEmpty();
    }

    [Test]
    public async Task UserSettings_WhenGlobalSourceAndPackTrustValid_IsAccepted()
    {
        var source = new ConfiguredSourceIdentity
        {
            Type = "local",
            Path = ProjectPath.Normalize(Path.GetFullPath("packs")),
        };
        var settings = new UserSettings
        {
            Global = new UserTrust
            {
                Sources = [source],
                Packs = [new TrustedPackIdentity { Id = "example", Source = source }],
            },
        };

        var issues = ManifestModelValidator.Validate(settings);

        await Assert.That(issues).IsEmpty();
    }

    [Test]
    public async Task UserSettings_WhenCanonicalProjectKeyValid_IsAccepted()
    {
        var projectPath = ProjectPath.Normalize(Path.GetFullPath("project"));
        var settings = new UserSettings
        {
            Projects = new Dictionary<string, LocalProjectTrust>(StringComparer.Ordinal)
            {
                [projectPath] = new(),
            },
        };

        var issues = ManifestModelValidator.Validate(settings);

        await Assert.That(issues).IsEmpty();
    }

    [Test]
    public async Task UserSettings_WhenTrustEntriesDuplicated_IsRejected()
    {
        var source = new ConfiguredSourceIdentity
        {
            Type = "local",
            Path = ProjectPath.Normalize(Path.GetFullPath("packs")),
        };
        var pack = new TrustedPackIdentity { Id = "example", Source = source };
        var settings = new UserSettings
        {
            Global = new UserTrust { Sources = [source, source], Packs = [pack, pack] },
        };

        var issues = ManifestModelValidator.Validate(settings);

        await Assert.That(issues).IsNotEmpty();
    }

    private static ProjectConfiguration CreateConfigurationWithSource() =>
        new()
        {
            SchemaVersion = 1,
            Sources = [new ProjectConfiguration.LocalSource { Name = "local", Path = "packs" }],
        };

    private static PackManifest CreateValidPackManifest() =>
        new()
        {
            Id = "example",
            Version = "1.0.0",
            Author = "Lunaris Digital Solutions",
            License = "MIT",
            ManagedFiles =
            [
                new PackManifest.PackManagedFile { Source = "source.txt", Target = "target.txt" },
            ],
        };
}
