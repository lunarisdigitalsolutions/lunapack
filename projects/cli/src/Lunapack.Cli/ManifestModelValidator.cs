using System.Collections;
using System.Text.RegularExpressions;

namespace Lunapack.Cli;

internal static partial class ManifestModelValidator
{
    private static readonly string[] _lifecycleHookNames =
    [
        "preInstall",
        "postInstall",
        "preUpdate",
        "postUpdate",
        "preUninstall",
        "postUninstall",
    ];

    private const int MaximumTagCount = 15;

    public static IReadOnlyList<string> Validate(PackManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);

        var issues = new List<string>();
        ValidateRequiredValue(manifest.Id, "id", issues);
        ValidatePackId(manifest.Id, "Pack", issues);
        ValidateRequiredValue(manifest.Version, "version", issues);
        ValidateRequiredMetadata(manifest.Author, "author", issues);
        ValidateRequiredMetadata(manifest.License, "license", issues);
        ValidateOptionalValue(manifest.Name, "name", issues);
        ValidateOptionalValue(manifest.Author, "author", issues);
        ValidateOptionalValue(manifest.License, "license", issues);
        ValidateHomepage(manifest.Homepage, issues);

        if (!IsSemanticVersion(manifest.Version))
        {
            issues.Add($"Version '{manifest.Version}' is not a valid semantic version.");
        }

        ValidateTags(manifest.Tags, issues);
        ValidateParameters(manifest.Parameters, issues);
        ValidatePackSources(manifest.Sources, issues);
        ValidateManagedFiles(manifest.ManagedFiles, manifest.Sources, issues);
        ValidatePackReferences(manifest.Packs, issues);
        ValidateHooks(manifest.Hooks, issues);

        return issues;
    }

    private static void ValidateOptionalValue(
        string? value,
        string propertyName,
        List<string> issues
    )
    {
        if (value is not null && value.Length == 0)
        {
            issues.Add($"Pack {propertyName} cannot be empty.");
        }
    }

    private static void ValidateHomepage(string? homepage, List<string> issues)
    {
        if (
            homepage is not null
            && (
                !Uri.TryCreate(homepage, UriKind.Absolute, out var uri)
                || uri.Scheme is not ("http" or "https")
            )
        )
        {
            issues.Add("Pack homepage must be an absolute HTTP or HTTPS URI.");
        }
    }

    private static void ValidateRequiredValue(
        string? value,
        string propertyName,
        List<string> issues
    )
    {
        if (string.IsNullOrEmpty(value))
        {
            issues.Add($"Pack {propertyName} is required.");
        }
    }

    private static void ValidateRequiredMetadata(
        string? value,
        string propertyName,
        List<string> issues
    )
    {
        if (value is null)
        {
            issues.Add($"Pack {propertyName} is required.");
        }
    }

    private static void ValidatePackId(string? id, string subject, List<string> issues)
    {
        if (!string.IsNullOrEmpty(id) && !PackIdRegex().IsMatch(id))
        {
            issues.Add($"{subject} ID '{id}' must use hyphen-separated alphanumeric segments.");
        }
    }

    private static void ValidateTags(List<string> tags, List<string> issues)
    {
        if (tags.Count > MaximumTagCount)
        {
            issues.Add($"Pack cannot define more than {MaximumTagCount} tags.");
        }

        if (
            tags.Any(string.IsNullOrEmpty)
            || tags.Distinct(StringComparer.Ordinal).Count() != tags.Count
        )
        {
            issues.Add("Pack tags must be non-empty and unique.");
        }
    }

    private static void ValidateParameters(
        IReadOnlyDictionary<string, PackManifest.PackParameter> parameters,
        List<string> issues
    )
    {
        foreach (var (name, parameter) in parameters)
        {
            if (!ParameterNameRegex().IsMatch(name))
            {
                issues.Add($"Parameter '{name}' has an invalid name.");
            }

            if (parameter is null)
            {
                issues.Add($"Parameter '{name}' is required.");
                continue;
            }

            if (parameter.Type is not ("string" or "bool" or "enum"))
            {
                issues.Add($"Parameter '{name}' has an invalid type.");
            }

            if (
                parameter.Multiple is not null
                && !string.Equals(parameter.Type, "enum", StringComparison.Ordinal)
            )
            {
                issues.Add($"Parameter '{name}' can only set multiple for enum values.");
            }

            ValidateParameterDefault(name, parameter, issues);

            if (
                string.Equals(parameter.Description, string.Empty, StringComparison.Ordinal)
                || string.Equals(parameter.DisplayName, string.Empty, StringComparison.Ordinal)
            )
            {
                issues.Add($"Parameter '{name}' metadata cannot be empty.");
            }

            if (string.Equals(parameter.Type, "enum", StringComparison.Ordinal))
            {
                if (
                    parameter.Values is not { Count: > 0 }
                    || parameter.Values.Any(string.IsNullOrEmpty)
                    || parameter.Values.Distinct(StringComparer.Ordinal).Count()
                        != parameter.Values.Count
                )
                {
                    issues.Add($"Enum parameter '{name}' must define unique values.");
                }
            }
            else if (parameter.Values is not null)
            {
                issues.Add($"Parameter '{name}' cannot define values.");
            }
        }
    }

    private static void ValidateParameterDefault(
        string name,
        PackManifest.PackParameter parameter,
        List<string> issues
    )
    {
        var isMultiSelect =
            string.Equals(parameter.Type, "enum", StringComparison.Ordinal)
            && parameter.Multiple is true;
        if (
            parameter.Default is not null
            && (
                string.Equals(parameter.Type, "bool", StringComparison.Ordinal)
                    && parameter.Default is not bool
                || string.Equals(parameter.Type, "string", StringComparison.Ordinal)
                    && parameter.Default is not string
                || string.Equals(parameter.Type, "enum", StringComparison.Ordinal)
                    && !isMultiSelect
                    && parameter.Default is not string
                || isMultiSelect && !TryGetUniqueStringValues(parameter.Default, out _)
            )
        )
        {
            issues.Add($"Parameter '{name}' has a default value incompatible with its type.");
        }

        if (
            string.Equals(parameter.Type, "enum", StringComparison.Ordinal)
            && parameter.Default is string defaultValue
            && parameter.Values is { } values
            && !values.Contains(defaultValue, StringComparer.Ordinal)
        )
        {
            issues.Add($"Enum parameter '{name}' default must be one of its values.");
        }

        if (
            isMultiSelect
            && parameter.Default is not null
            && TryGetUniqueStringValues(parameter.Default, out var defaultValues)
            && parameter.Values is { } allowedValues
            && defaultValues.Any(value => !allowedValues.Contains(value, StringComparer.Ordinal))
        )
        {
            issues.Add($"Enum parameter '{name}' defaults must be among its values.");
        }
    }

    private static void ValidatePackSources(
        IReadOnlyDictionary<string, PackManifest.PackSource> sources,
        List<string> issues
    )
    {
        foreach (var (alias, source) in sources)
        {
            if (!IsSourceAlias(alias))
            {
                issues.Add(
                    $"Pack source alias '{alias}' must use alphanumeric segments separated by '.', '_', or '-'."
                );
            }

            if (source is null)
            {
                issues.Add($"Pack source '{alias}' is required.");
                continue;
            }

            ValidatePackSource(alias, source, issues);
        }
    }

    private static void ValidatePackSource(
        string alias,
        PackManifest.PackSource source,
        List<string> issues
    )
    {
        if (!string.Equals(source.Type, "git", StringComparison.Ordinal))
        {
            issues.Add($"Pack source '{alias}' must declare type 'git'.");
        }

        if (string.IsNullOrEmpty(source.Ref))
        {
            issues.Add($"Pack source '{alias}' must pin a ref.");
        }

        var repository = SourceIdentityNormalizer.NormalizeRepository(source.Url);
        if (!repository.IsSuccess)
        {
            issues.Add($"Pack source '{alias}' URL is invalid: {repository.Error}");
        }

        if (ContainsCredentialPlaceholder(source.Url))
        {
            issues.Add($"Pack source '{alias}' URL must not embed credentials.");
        }

        if (source.Path is not null && !IsSafeProjectRelativePath(source.Path))
        {
            issues.Add($"Pack source '{alias}' path must stay inside the repository.");
        }

        if (source.Description is "")
        {
            issues.Add($"Pack source '{alias}' description cannot be empty.");
        }
    }

    private static bool ContainsCredentialPlaceholder(string? url) =>
        url is not null
        && (
            url.Contains("${", StringComparison.Ordinal)
            || url.Contains("$(", StringComparison.Ordinal)
        );

    private static void ValidateManagedFiles(
        IReadOnlyList<PackManifest.PackManagedFile> managedFiles,
        IReadOnlyDictionary<string, PackManifest.PackSource> sources,
        List<string> issues
    )
    {
        foreach (var managedFile in managedFiles)
        {
            if (managedFile is null)
            {
                issues.Add("Managed file is required.");
                continue;
            }

            if (string.IsNullOrEmpty(managedFile.Target))
            {
                issues.Add("Managed file target is required.");
            }

            if (managedFile.Condition is "")
            {
                issues.Add("Managed file condition cannot be empty.");
            }

            ValidateSelector(managedFile, sources, issues);
            ValidateManagedFileStrategy(managedFile.Strategy, issues);
        }
    }

    private static void ValidateSelector(
        PackManifest.PackManagedFile managedFile,
        IReadOnlyDictionary<string, PackManifest.PackSource> sources,
        List<string> issues
    )
    {
        if (
            managedFile.Source is ""
            || managedFile.Glob is ""
            || managedFile.Directory is ""
            || managedFile.Path is ""
        )
        {
            issues.Add("Managed file selector cannot be empty.");
            return;
        }

        var createdSelector = PackManagedFileSelector.Create(managedFile);
        if (createdSelector.Value is not { } selector)
        {
            issues.Add(createdSelector.Error ?? "Managed file selector is invalid.");
            return;
        }

        ValidateSelectorSourceAlias(managedFile, selector, sources, issues);
        ValidateSelectorPaths(selector, issues);
    }

    private static void ValidateSelectorSourceAlias(
        PackManifest.PackManagedFile managedFile,
        PackManagedFileSelector selector,
        IReadOnlyDictionary<string, PackManifest.PackSource> sources,
        List<string> issues
    )
    {
        if (selector.SourceAlias is { } alias)
        {
            if (!sources.ContainsKey(alias))
            {
                issues.Add($"Managed file references undeclared pack source '{alias}'.");
            }

            return;
        }

        if (
            managedFile.Source is { } legacySource
            && managedFile.Path is null
            && sources.ContainsKey(legacySource)
        )
        {
            issues.Add(
                $"Managed file referencing pack source '{legacySource}' must declare 'path'."
            );
        }
    }

    private static void ValidateSelectorPaths(PackManagedFileSelector selector, List<string> issues)
    {
        if (
            selector.Kind != PackManagedFileSelectorKind.Glob
            && !IsSafeProjectRelativePath(selector.Value)
        )
        {
            issues.Add($"Managed file selector '{selector.Value}' must stay inside its source.");
        }

        foreach (var exclusion in selector.Exclusions)
        {
            if (string.IsNullOrEmpty(exclusion) || !IsSafeProjectRelativePath(exclusion))
            {
                issues.Add("Managed file exclusions must be non-empty source-relative patterns.");
            }
        }

        if (
            selector.Exclusions.Distinct(StringComparer.Ordinal).Count()
            != selector.Exclusions.Count
        )
        {
            issues.Add("Managed file exclusions must be unique.");
        }
    }

    private static void ValidateManagedFileStrategy(
        PackManifest.PackManagedFileStrategy? strategy,
        List<string> issues
    )
    {
        if (
            strategy is null
            || (strategy.Type, strategy.Method)
                is not (

                    ("copy", "overwrite")
                    or
                    ("copy", "fail-if-exists")
                    or
                    ("copy", "skip-if-exists")
                    or
                    ("copy", "backup-and-overwrite")
                    or
                    ("merge", "lines")
                    or
                    ("merge", "section")
                    or
                    ("merge", "json")
                )
        )
        {
            issues.Add("Managed file strategy has an invalid type and method combination.");
        }
    }

    private static void ValidatePackReferences(
        IReadOnlyList<PackManifest.PackReference> packReferences,
        List<string> issues
    )
    {
        foreach (var packReference in packReferences)
        {
            if (packReference is null)
            {
                issues.Add("Pack reference is required.");
                continue;
            }

            if (string.IsNullOrEmpty(packReference.Id) || !IsSemanticVersion(packReference.Version))
            {
                issues.Add("Pack reference must define an ID and semantic version.");
            }
            else
            {
                ValidatePackId(packReference.Id, "Pack reference", issues);
            }

            foreach (var (name, value) in packReference.Parameters)
            {
                if (
                    !ParameterNameRegex().IsMatch(name)
                    || value is not string and not bool && !TryGetUniqueStringValues(value, out _)
                )
                {
                    issues.Add(
                        $"Pack reference parameter '{name}' must be a named string, Boolean, or unique string array."
                    );
                }
            }

            if (
                packReference.DisabledHooks.Distinct(StringComparer.Ordinal).Count()
                    != packReference.DisabledHooks.Count
                || packReference.DisabledHooks.Any(hook =>
                    !_lifecycleHookNames.Contains(hook, StringComparer.Ordinal)
                )
            )
            {
                issues.Add(
                    $"Pack reference '{packReference.Id}' disabled hooks must be unique lifecycle types."
                );
            }
        }
    }

    private static void ValidateHooks(PackManifest.PackHooks? hooks, List<string> issues)
    {
        if (hooks is null)
        {
            return;
        }

        ValidateHooks("postInstall", hooks.PostInstall, issues);
        ValidateHooks("postUninstall", hooks.PostUninstall, issues);
        ValidateHooks("postUpdate", hooks.PostUpdate, issues);
        ValidateHooks("preInstall", hooks.PreInstall, issues);
        ValidateHooks("preUninstall", hooks.PreUninstall, issues);
        ValidateHooks("preUpdate", hooks.PreUpdate, issues);
    }

    private static void ValidateHooks(
        string eventName,
        List<PackManifest.PackHook>? hooks,
        List<string> issues
    )
    {
        if (hooks is null)
        {
            return;
        }

        if (hooks.Count == 0)
        {
            issues.Add($"Lifecycle hook event '{eventName}' must not be empty.");
        }

        foreach (var hook in hooks)
        {
            if (hook is null)
            {
                issues.Add($"Lifecycle hook event '{eventName}' contains an invalid declaration.");
                continue;
            }

            switch (hook.Type)
            {
                case "script":
                    ValidateScriptHook(eventName, hook, issues);
                    break;
                case "instruction":
                    ValidateInstructionHook(eventName, hook, issues);
                    break;
                default:
                    issues.Add($"Lifecycle hook event '{eventName}' has an invalid type.");
                    break;
            }
        }
    }

    private static void ValidateScriptHook(
        string eventName,
        PackManifest.PackHook hook,
        List<string> issues
    )
    {
        var hasCommand = hook.Command is not null;
        var hasFile = hook.File is not null;
        var hasRunner = hook.Runner is not null;
        if (hasCommand == hasFile || hasFile != hasRunner)
        {
            issues.Add(
                $"Script hook in '{eventName}' must define either command or file and runner."
            );
        }

        if (hook.Command is "" || hook.File is "" || hook.Runner is "")
        {
            issues.Add($"Script hook in '{eventName}' execution values cannot be empty.");
        }

        if (hook.Description is "")
        {
            issues.Add($"Script hook in '{eventName}' description cannot be empty.");
        }

        if (hook.File is not null && !IsSafeProjectRelativePath(hook.File))
        {
            issues.Add($"Script hook in '{eventName}' file must be a safe relative path.");
        }

        if (hook.Templating is not null)
        {
            issues.Add($"Script hook in '{eventName}' cannot define templating.");
        }
    }

    private static void ValidateInstructionHook(
        string eventName,
        PackManifest.PackHook hook,
        List<string> issues
    )
    {
        if (
            string.IsNullOrEmpty(hook.File)
            || !IsSafeProjectRelativePath(hook.File)
            || !hook.File.EndsWith(".md", StringComparison.Ordinal)
        )
        {
            issues.Add(
                $"Instruction hook in '{eventName}' file must be a safe relative Markdown path."
            );
        }

        if (
            hook.Command is not null
            || hook.Runner is not null
            || hook.Arguments.Count > 0
            || hook.Description is not null
        )
        {
            issues.Add($"Instruction hook in '{eventName}' has unsupported properties.");
        }
    }

    public static bool IsSemanticVersion(string? value) =>
        value is not null && SemanticVersionRegex().IsMatch(value);

    public static bool IsPackId(string? value) =>
        !string.IsNullOrEmpty(value) && PackIdRegex().IsMatch(value);

    public static IReadOnlyList<string> Validate(ProjectConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var issues = new List<string>();
        if (configuration.SchemaVersion != 1)
        {
            issues.Add("Project configuration schema version must be 1.");
        }

        var sourceNames = configuration
            .Sources.Select(source => source.Name)
            .ToHashSet(StringComparer.Ordinal);
        if (
            sourceNames.Count != configuration.Sources.Count
            || sourceNames.Any(string.IsNullOrEmpty)
        )
        {
            issues.Add("Project sources must have non-empty unique names.");
        }

        foreach (var source in configuration.Sources)
        {
            switch (source)
            {
                case ProjectConfiguration.LocalSource localSource:
                    if (
                        !string.Equals(localSource.Type, "local", StringComparison.Ordinal)
                        || !IsRelativePath(localSource.Path)
                    )
                    {
                        issues.Add("Local sources must have type 'local' and a relative path.");
                    }

                    break;
                case ProjectConfiguration.GitSource gitSource:
                    ValidateGitSource(gitSource, issues);
                    break;
                default:
                    issues.Add("Project configuration contains an unsupported source type.");
                    break;
            }
        }

        ValidateSourceFingerprintUniqueness(configuration.Sources, issues);
        ValidateRequestedPacks(configuration.Packs, issues);
        ValidateLinks(configuration.Links, sourceNames, issues);
        ValidateProjectTrust(configuration.Trust, sourceNames, issues);
        ValidateVariables(configuration.Variables, issues);

        return issues;
    }

    private static void ValidateSourceFingerprintUniqueness(
        IReadOnlyList<ProjectConfiguration.Source> sources,
        List<string> issues
    )
    {
        var fingerprints = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var source in sources)
        {
            var created = SourceIdentityNormalizer.Create(source);
            if (created.Value is not { } fingerprint)
            {
                continue;
            }

            if (fingerprints.TryGetValue(fingerprint.Value, out var existingName))
            {
                issues.Add(
                    $"Source '{source.Name}' duplicates source '{existingName}' after canonicalization."
                );
                continue;
            }

            fingerprints.Add(fingerprint.Value, source.Name);
        }
    }

    private static void ValidateLinks(
        Dictionary<string, ProjectConfiguration.Link> links,
        HashSet<string> configuredSourceNames,
        List<string> issues
    )
    {
        foreach (var (name, link) in links)
        {
            if (!IsPackId(name))
            {
                issues.Add($"Link name '{name}' must use pack-ID syntax.");
            }

            if (string.IsNullOrEmpty(link.Source) || !configuredSourceNames.Contains(link.Source))
            {
                issues.Add($"Link '{name}' must reference a configured source name.");
            }

            if (link.Includes.Count == 0 || link.Includes.Any(string.IsNullOrEmpty))
            {
                issues.Add($"Link '{name}' must declare at least one non-empty include pattern.");
            }

            if (
                link
                    .Includes.Concat(link.Excludes)
                    .Any(pattern => !IsSafeProjectRelativePath(pattern))
            )
            {
                issues.Add($"Link '{name}' patterns must be safe source-relative paths.");
            }

            if (
                (link.Path is not null && !IsSafeProjectRelativePath(link.Path))
                || (link.Target is not null && !IsSafeProjectRelativePath(link.Target))
                || (link.StripPrefix is not null && !IsSafeProjectRelativePath(link.StripPrefix))
            )
            {
                issues.Add($"Link '{name}' paths must be safe relative paths.");
            }

            if (link.Ref is { Length: 0 })
            {
                issues.Add($"Link '{name}' ref must not be empty.");
            }
        }
    }

    private static void ValidateProjectTrust(
        ProjectConfiguration.ProjectTrust trust,
        HashSet<string> configuredSourceNames,
        List<string> issues
    )
    {
        if (
            trust.Sources.Any(source =>
                string.IsNullOrEmpty(source) || !configuredSourceNames.Contains(source)
            )
            || trust.Sources.Distinct(StringComparer.Ordinal).Count() != trust.Sources.Count
        )
        {
            issues.Add("Trusted sources must be unique configured source names.");
        }

        if (
            trust.Packs.Any(pack =>
                string.IsNullOrEmpty(pack.Id)
                || pack.Id.Contains('@')
                || !configuredSourceNames.Contains(pack.Source)
            )
            || trust.Packs.Select(pack => (pack.Source, pack.Id)).Distinct().Count()
                != trust.Packs.Count
        )
        {
            issues.Add(
                "Trusted packs must be unique bare pack IDs bound to configured source names."
            );
        }
    }

    public static IReadOnlyList<string> Validate(ProjectLockFile lockFile)
    {
        ArgumentNullException.ThrowIfNull(lockFile);

        var issues = new List<string>();
        if (lockFile.SchemaVersion != 1)
        {
            issues.Add("Project lock file schema version must be 1.");
        }

        foreach (var resolvedPack in lockFile.Packs)
        {
            ValidateResolvedPack(resolvedPack, issues);
        }

        foreach (var (name, resolvedLink) in lockFile.Links)
        {
            ValidateResolvedLink(name, resolvedLink, issues);
        }

        return issues;
    }

    private static void ValidateResolvedLink(
        string name,
        ProjectLockFile.ResolvedLink resolvedLink,
        List<string> issues
    )
    {
        if (!IsPackId(name))
        {
            issues.Add($"Resolved link name '{name}' must use pack-ID syntax.");
        }

        if (string.IsNullOrEmpty(resolvedLink.SourceName) || resolvedLink.SourceIdentity is null)
        {
            issues.Add($"Resolved link '{name}' must define source name and identity.");
        }

        if (!IsSha256(resolvedLink.DefinitionSha256))
        {
            issues.Add($"Resolved link '{name}' must define a SHA-256 definition hash.");
        }

        if (resolvedLink.GitSource is { } gitSource && !IsGitCommit(gitSource.ResolvedCommit))
        {
            issues.Add($"Resolved link '{name}' must record a resolved Git commit.");
        }

        foreach (var file in resolvedLink.Files)
        {
            if (
                !IsSafeProjectRelativePath(file.SourcePath)
                || !IsSafeProjectRelativePath(file.DeclaredTargetPath)
                || !IsRelativePath(file.TargetPath)
                || !IsSha256(file.Sha256)
            )
            {
                issues.Add(
                    $"Resolved link '{name}' files must define safe source, declared, and effective target paths and a SHA-256 hash."
                );
            }
        }
    }

    public static IReadOnlyList<string> Validate(UserSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var issues = new List<string>();
        ValidateTrustEntries(
            settings.Global.Sources,
            settings.Global.Packs,
            "Global user trust",
            issues
        );

        foreach (var (projectPath, projectTrust) in settings.Projects)
        {
            if (!IsCanonicalAbsolutePath(projectPath))
            {
                issues.Add($"Local project trust key '{projectPath}' is not a canonical path.");
            }

            ValidateTrustEntries(
                projectTrust.Sources,
                projectTrust.Packs,
                $"Local project trust '{projectPath}'",
                issues
            );
            ValidateTrustEntries(
                projectTrust.Acknowledgements.Sources,
                projectTrust.Acknowledgements.Packs,
                $"Project trust acknowledgement '{projectPath}'",
                issues
            );
        }

        return issues;
    }

    private static void ValidateTrustEntries(
        List<ConfiguredSourceIdentity> sources,
        List<TrustedPackIdentity> packs,
        string context,
        List<string> issues
    )
    {
        if (
            sources.Any(source => !IsValidSourceIdentity(source))
            || sources.Distinct().Count() != sources.Count
        )
        {
            issues.Add($"{context} sources must contain unique configured-source identities.");
        }

        if (
            packs.Any(pack =>
                string.IsNullOrEmpty(pack.Id)
                || pack.Id.Contains('@')
                || !IsValidSourceIdentity(pack.Source)
            )
            || packs.Distinct().Count() != packs.Count
        )
        {
            issues.Add(
                $"{context} packs must contain unique bare IDs bound to configured-source identities."
            );
        }
    }

    private static bool IsValidSourceIdentity(ConfiguredSourceIdentity source) =>
        source.Type switch
        {
            "local" => IsCanonicalAbsolutePath(source.Path)
                && source.Url is null
                && source.Ref is null,
            "git" => !string.IsNullOrEmpty(source.Url)
                && source.Ref is not ""
                && (source.Path is null || IsSafeProjectRelativePath(source.Path)),
            _ => false,
        };

    private static bool IsCanonicalAbsolutePath(string? value)
    {
        if (string.IsNullOrEmpty(value) || !Path.IsPathFullyQualified(value))
        {
            return false;
        }

        try
        {
            var canonicalPath = ProjectPath.Normalize(
                Path.TrimEndingDirectorySeparator(Path.GetFullPath(value))
            );
            var suppliedPath = ProjectPath.Normalize(Path.TrimEndingDirectorySeparator(value));
            return string.Equals(canonicalPath, suppliedPath, StringComparison.Ordinal);
        }
        catch (Exception exception)
            when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    public static IReadOnlyList<string> Validate(ProjectManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);

        var issues = new List<string>();
        if (manifest.SchemaVersion != 1)
        {
            issues.Add("Project manifest schema version must be 1.");
        }

        foreach (var source in manifest.Sources)
        {
            if (
                !string.Equals(source.Type, "local", StringComparison.Ordinal)
                || !IsRelativePath(source.Path)
            )
            {
                issues.Add("Project manifest sources must be local relative paths.");
            }
        }

        foreach (var installedPack in manifest.Packs)
        {
            if (
                string.IsNullOrEmpty(installedPack.Id)
                || !IsSemanticVersion(installedPack.Version)
                || !IsRelativePath(installedPack.SourcePath)
            )
            {
                issues.Add(
                    "Installed packs must define an ID, semantic version, and relative source path."
                );
            }

            foreach (var managedFile in installedPack.ManagedFiles)
            {
                if (!IsRelativePath(managedFile.TargetPath) || !IsSha256(managedFile.Sha256))
                {
                    issues.Add(
                        "Installed managed files must define a relative target path and SHA-256 hash."
                    );
                }
            }
        }

        return issues;
    }

    private static void ValidateGitSource(
        ProjectConfiguration.GitSource source,
        List<string> issues
    )
    {
        if (
            !string.Equals(source.Type, "git", StringComparison.Ordinal)
            || string.IsNullOrEmpty(source.Url)
            || source.Ref is ""
            || (source.Path is not null && !IsSafeProjectRelativePath(source.Path))
            || source.TimeoutSeconds is < 1 or > 300
        )
        {
            issues.Add("Git sources must define a URL and valid optional ref, path, and timeout.");
        }
    }

    private static void ValidateRequestedPacks(
        IReadOnlyList<ProjectConfiguration.RequestedPack> requestedPacks,
        List<string> issues
    )
    {
        foreach (var requestedPack in requestedPacks)
        {
            if (string.IsNullOrEmpty(requestedPack.Id))
            {
                issues.Add("Requested packs must define an ID.");
            }

            if (requestedPack.Version is not null && !IsSemanticVersion(requestedPack.Version))
            {
                issues.Add($"Requested pack '{requestedPack.Id}' has an invalid version.");
            }

            if (
                requestedPack.Destination is not null
                && !IsSafeProjectRelativePath(requestedPack.Destination)
            )
            {
                issues.Add($"Requested pack '{requestedPack.Id}' has an unsafe destination.");
            }
        }
    }

    private static void ValidateVariables(
        IReadOnlyDictionary<string, object> variables,
        List<string> issues
    )
    {
        foreach (var (name, value) in variables)
        {
            if (
                !ParameterNameRegex().IsMatch(name)
                || value is not string and not bool && !TryGetUniqueStringValues(value, out _)
            )
            {
                issues.Add(
                    $"Variable '{name}' must be a named string, Boolean, or unique string array."
                );
            }
        }
    }

    private static bool TryGetUniqueStringValues(
        object value,
        out IReadOnlyList<string> stringValues
    )
    {
        if (value is string || value is not IEnumerable values)
        {
            stringValues = [];
            return false;
        }

        var materializedValues = new List<string>();
        var uniqueValues = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in values)
        {
            if (item is not string stringValue || !uniqueValues.Add(stringValue))
            {
                stringValues = [];
                return false;
            }

            materializedValues.Add(stringValue);
        }

        stringValues = materializedValues;
        return true;
    }

    private static void ValidateResolvedPack(
        ProjectLockFile.ResolvedPack resolvedPack,
        List<string> issues
    )
    {
        if (
            string.IsNullOrEmpty(resolvedPack.Id)
            || !IsSemanticVersion(resolvedPack.Version)
            || !IsRelativePath(resolvedPack.PackPath)
            || (
                resolvedPack.Destination is not null
                && !IsSafeProjectRelativePath(resolvedPack.Destination)
            )
        )
        {
            issues.Add(
                "Resolved packs must define an ID, semantic version, safe destination, and relative pack path."
            );
        }

        if (string.IsNullOrEmpty(resolvedPack.SourceName) || resolvedPack.SourceIdentity is null)
        {
            issues.Add("Resolved packs must define source name and identity.");
        }

        if (resolvedPack.GitSource is null)
        {
            ValidateLocalResolvedPackSource(resolvedPack, issues);
        }
        else
        {
            ValidateGitResolvedPackSource(resolvedPack, issues);
        }

        foreach (var packReference in resolvedPack.Packs)
        {
            if (string.IsNullOrEmpty(packReference.Id) || !IsSemanticVersion(packReference.Version))
            {
                issues.Add("Resolved pack references must define an ID and semantic version.");
            }
        }

        ValidateResolvedPackExternalSources(resolvedPack, issues);

        ValidateResolvedManagedFiles(resolvedPack, issues);
    }

    private static void ValidateResolvedManagedFiles(
        ProjectLockFile.ResolvedPack resolvedPack,
        List<string> issues
    )
    {
        foreach (var managedFile in resolvedPack.ManagedFiles)
        {
            if (
                !IsSafeProjectRelativePath(managedFile.DeclaredTargetPath)
                || !IsRelativePath(managedFile.TargetPath)
                || !IsSha256(managedFile.Sha256)
                || (
                    managedFile.Strategy is not null
                    && (
                        string.IsNullOrEmpty(managedFile.Strategy.Type)
                        || string.IsNullOrEmpty(managedFile.Strategy.Method)
                    )
                )
            )
            {
                issues.Add(
                    "Resolved managed files must define safe declared and effective target paths and a SHA-256 hash."
                );
            }

            ValidateResolvedManagedFileProvenance(resolvedPack, managedFile, issues);
        }
    }

    private static void ValidateResolvedPackExternalSources(
        ProjectLockFile.ResolvedPack resolvedPack,
        List<string> issues
    )
    {
        foreach (var (alias, externalSource) in resolvedPack.ExternalSources)
        {
            if (!IsSourceAlias(alias))
            {
                issues.Add(
                    $"Resolved pack '{resolvedPack.Id}' external source alias '{alias}' is invalid."
                );
            }

            if (externalSource is null)
            {
                issues.Add(
                    $"Resolved pack '{resolvedPack.Id}' external source '{alias}' is required."
                );
                continue;
            }

            if (
                string.IsNullOrEmpty(externalSource.SourceName)
                || string.IsNullOrEmpty(externalSource.Ref)
                || !IsSourceFingerprint(externalSource.Fingerprint)
                || !IsGitCommit(externalSource.ResolvedCommit)
            )
            {
                issues.Add(
                    $"Resolved pack '{resolvedPack.Id}' external source '{alias}' must record source name, fingerprint, ref, and resolved commit."
                );
            }
        }
    }

    private static void ValidateResolvedManagedFileProvenance(
        ProjectLockFile.ResolvedPack resolvedPack,
        ProjectLockFile.ManagedFile managedFile,
        List<string> issues
    )
    {
        var provided = new[]
        {
            managedFile.SourceAlias,
            managedFile.SourceName,
            managedFile.SourceFingerprint,
            managedFile.SourcePath,
        }.Count(static value => value is not null);
        if (provided == 0)
        {
            return;
        }

        if (provided != 4)
        {
            issues.Add(
                "Resolved managed files with external provenance must record source alias, source name, fingerprint, and source path."
            );
            return;
        }

        if (
            !IsSourceAlias(managedFile.SourceAlias)
            || string.IsNullOrEmpty(managedFile.SourceName)
            || !IsSourceFingerprint(managedFile.SourceFingerprint)
            || !IsSafeProjectRelativePath(managedFile.SourcePath)
        )
        {
            issues.Add("Resolved managed file external provenance is invalid.");
            return;
        }

        if (!resolvedPack.ExternalSources.TryGetValue(managedFile.SourceAlias!, out var declared))
        {
            issues.Add(
                $"Resolved managed file references external source '{managedFile.SourceAlias}' that pack '{resolvedPack.Id}' does not record."
            );
            return;
        }

        if (
            !string.Equals(
                declared.Fingerprint,
                managedFile.SourceFingerprint,
                StringComparison.Ordinal
            )
            || !string.Equals(declared.SourceName, managedFile.SourceName, StringComparison.Ordinal)
        )
        {
            issues.Add(
                $"Resolved managed file provenance for '{managedFile.SourceAlias}' does not match the recorded external source."
            );
        }
    }

    private static bool IsSourceFingerprint(string? value) =>
        !string.IsNullOrEmpty(value)
        && (
            value.StartsWith("git:", StringComparison.Ordinal)
            || value.StartsWith("local:", StringComparison.Ordinal)
        );

    private static void ValidateLocalResolvedPackSource(
        ProjectLockFile.ResolvedPack resolvedPack,
        List<string> issues
    )
    {
        if (!IsRelativePath(resolvedPack.SourcePath))
        {
            issues.Add("Local resolved packs must define a relative source path.");
        }

        if (
            resolvedPack.SourceIdentity is { } identity
            && (
                !string.Equals(identity.Type, "local", StringComparison.Ordinal)
                || !IsRelativePath(identity.Path)
                || identity.Url is not null
                || identity.Ref is not null
                || !string.Equals(identity.Path, resolvedPack.SourcePath, StringComparison.Ordinal)
            )
        )
        {
            issues.Add("Local resolved pack source identity is invalid.");
        }
    }

    private static void ValidateGitResolvedPackSource(
        ProjectLockFile.ResolvedPack resolvedPack,
        List<string> issues
    )
    {
        var provenance = resolvedPack.GitSource!;
        if (resolvedPack.SourcePath is not null)
        {
            issues.Add("Git resolved packs cannot define a local source path.");
        }

        ValidateGitSource(provenance, issues);
        if (
            resolvedPack.SourceIdentity is { } identity
            && (
                !string.Equals(identity.Type, "git", StringComparison.Ordinal)
                || string.IsNullOrEmpty(identity.Url)
                || identity.Ref is ""
                || (identity.Path is not null && !IsSafeProjectRelativePath(identity.Path))
                || !string.Equals(identity.Url, provenance.Url, StringComparison.Ordinal)
                || !string.Equals(identity.Ref, provenance.Ref, StringComparison.Ordinal)
                || !string.Equals(identity.Path, provenance.Path, StringComparison.Ordinal)
            )
        )
        {
            issues.Add("Git resolved pack source identity is invalid.");
        }
    }

    private static void ValidateGitSource(GitSourceProvenance source, List<string> issues)
    {
        if (
            !string.Equals(source.Type, "git", StringComparison.Ordinal)
            || string.IsNullOrEmpty(source.Url)
            || source.Ref is ""
            || (source.Path is not null && !IsSafeProjectRelativePath(source.Path))
            || !IsGitCommit(source.ResolvedCommit)
        )
        {
            issues.Add(
                "Git provenance must define a URL, resolved commit, and valid optional ref and path."
            );
        }
    }

    private static bool IsRelativePath(string? value) =>
        !string.IsNullOrEmpty(value) && !IsAbsolutePath(value);

    private static bool IsSafeProjectRelativePath(string? value)
    {
        if (!IsRelativePath(value))
        {
            return false;
        }

        var relativePath = value!;
        return !relativePath
            .Split(['/', '\\'], StringSplitOptions.None)
            .Contains("..", StringComparer.Ordinal);
    }

    private static bool IsAbsolutePath(string value) =>
        value.StartsWith('/')
        || value.StartsWith('\\')
        || (
            value.Length >= 3
            && char.IsAsciiLetter(value[0])
            && value[1] == ':'
            && (value[2] == '/' || value[2] == '\\')
        );

    private static bool IsGitCommit(string? value) =>
        value is not null && GitCommitRegex().IsMatch(value);

    private static bool IsSha256(string? value) =>
        value is not null && Sha256Regex().IsMatch(value);

    public static bool IsSourceAlias(string? value) =>
        !string.IsNullOrEmpty(value) && SourceAliasRegex().IsMatch(value);

    [GeneratedRegex(
        "^[A-Za-z_][A-Za-z0-9_]*$",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking
    )]
    private static partial Regex ParameterNameRegex();

    [GeneratedRegex(
        "^[A-Za-z0-9]+(?:-[A-Za-z0-9]+)*$",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking
    )]
    private static partial Regex PackIdRegex();

    [GeneratedRegex(
        "^[0-9]+\\.[0-9]+\\.[0-9]+(?:-[0-9A-Za-z.-]+)?(?:\\+[0-9A-Za-z.-]+)?$",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking
    )]
    private static partial Regex SemanticVersionRegex();

    [GeneratedRegex(
        "^[A-Fa-f0-9]{40}(?:[A-Fa-f0-9]{24})?$",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking
    )]
    private static partial Regex GitCommitRegex();

    [GeneratedRegex(
        "^[A-Fa-f0-9]{64}$",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking
    )]
    private static partial Regex Sha256Regex();

    [GeneratedRegex(
        "^[A-Za-z0-9]+(?:[._-][A-Za-z0-9]+)*$",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking
    )]
    private static partial Regex SourceAliasRegex();
}
