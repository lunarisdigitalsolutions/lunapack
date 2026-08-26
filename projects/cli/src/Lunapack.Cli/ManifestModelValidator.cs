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
    ];

    private const int MaximumTagCount = 15;

    public static IReadOnlyList<string> Validate(PackManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);

        var issues = new List<string>();
        ValidateRequiredValue(manifest.Id, "id", issues);
        ValidateRequiredValue(manifest.Version, "version", issues);
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
        ValidateManagedFiles(manifest.ManagedFiles, issues);
        ValidatePackReferences(manifest.Packs, issues);
        ValidateScripts(manifest.Scripts, issues);

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

    private static void ValidateManagedFiles(
        IReadOnlyList<PackManifest.PackManagedFile> managedFiles,
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

            ValidateSelector(managedFile, issues);
            ValidateManagedFileStrategy(managedFile.Strategy, issues);
        }
    }

    private static void ValidateSelector(
        PackManifest.PackManagedFile managedFile,
        List<string> issues
    )
    {
        var selectorCount = new[]
        {
            managedFile.Source,
            managedFile.Glob,
            managedFile.Directory,
        }.Count(static selector => selector is not null);
        if (selectorCount != 1)
        {
            issues.Add("Managed file must define exactly one source, glob, or directory.");
            return;
        }

        if (managedFile.Source is "" || managedFile.Glob is "" || managedFile.Directory is "")
        {
            issues.Add("Managed file selector cannot be empty.");
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

            foreach (var (name, value) in packReference.Parameters)
            {
                if (!ParameterNameRegex().IsMatch(name) || value is not string and not bool)
                {
                    issues.Add(
                        $"Pack reference parameter '{name}' must be a named string or Boolean."
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

    private static void ValidateScripts(PackManifest.PackScripts? scripts, List<string> issues)
    {
        if (scripts is null)
        {
            return;
        }

        ValidateLifecycleScript("postInstall", scripts.PostInstall, issues);
        ValidateLifecycleScript("postUpdate", scripts.PostUpdate, issues);
        ValidateLifecycleScript("preInstall", scripts.PreInstall, issues);
        ValidateLifecycleScript("preUpdate", scripts.PreUpdate, issues);
    }

    private static void ValidateLifecycleScript(
        string hook,
        PackManifest.LifecycleScript? script,
        List<string> issues
    )
    {
        if (script is null)
        {
            return;
        }

        var hasCommand = script.Command is not null;
        var hasFile = script.File is not null;
        var hasRunner = script.Runner is not null;
        if (hasCommand == hasFile || hasFile != hasRunner)
        {
            issues.Add($"Lifecycle script '{hook}' must define either command or file and runner.");
        }

        if (script.Command is "" || script.File is "" || script.Runner is "")
        {
            issues.Add($"Lifecycle script '{hook}' execution values cannot be empty.");
        }

        if (script.Description is "")
        {
            issues.Add($"Lifecycle script '{hook}' description cannot be empty.");
        }

        if (script.File is not null && !IsSafeProjectRelativePath(script.File))
        {
            issues.Add($"Lifecycle script '{hook}' file must be a safe relative path.");
        }
    }

    public static bool IsSemanticVersion(string? value) =>
        value is not null && SemanticVersionRegex().IsMatch(value);

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

        ValidateRequestedPacks(configuration.Packs, issues);
        ValidateProjectTrust(configuration.Trust, sourceNames, issues);
        ValidateVariables(configuration.Variables, issues);

        return issues;
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

        return issues;
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
            if (!ParameterNameRegex().IsMatch(name) || value is not string and not bool)
            {
                issues.Add($"Variable '{name}' must be a named string or Boolean.");
            }
        }
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
        }
    }

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

    [GeneratedRegex(
        "^[A-Za-z_][A-Za-z0-9_]*$",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking
    )]
    private static partial Regex ParameterNameRegex();

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
}
