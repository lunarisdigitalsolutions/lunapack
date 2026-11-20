using System.IO.Abstractions;
using Lunapack.Cli.Application;
using Lunapack.Cli.Application.CommandExecution;
using Lunapack.Cli.Application.Preferences;
using Lunapack.Cli.Application.Serialization;
using Lunapack.Cli.Packs;
using Lunapack.Cli.Packs.Authoring;
using Lunapack.Cli.Packs.Manifest;
using Lunapack.Cli.Sources;
using NuGet.Versioning;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Lunapack.Cli.Catalog;

internal sealed class LocalPackDiscovery(IFileSystem fileSystem, CliConsole console)
{
    private static readonly IDeserializer _deserializer = new StaticDeserializerBuilder(
        new LunapackYamlContext()
    )
        .IgnoreUnmatchedProperties()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .WithTypeConverter(new PackParameterYamlTypeConverter())
        .WithTypeConverter(new ScalarValueDictionaryYamlTypeConverter())
        .Build();

    private readonly IFileSystem _fileSystem = fileSystem;
    private readonly CliConsole _console = console;

    public async Task<ManifestOperationResult<IReadOnlyList<CatalogPack>>> BrowseAsync(
        string sourcePath,
        int sourceOrder,
        string sourceName,
        ConfiguredSourceIdentity sourceIdentity,
        ResourceLimits? resourceLimits = null
    )
    {
        var effectiveResourceLimits = resourceLimits ?? ResourceLimits.Default;
        var manifests = EnumerateManifestPaths(sourcePath, effectiveResourceLimits.FilesPerGraph);
        if (manifests.Value is not { } manifestPaths)
        {
            return ManifestOperationResult<IReadOnlyList<CatalogPack>>.Failure(
                manifests.Error ?? "Unable to enumerate pack source.",
                manifests.ErrorKind
            );
        }

        var packs = new List<CatalogPack>(manifestPaths.Count);
        var manifestBudget = new CatalogManifestBudget(effectiveResourceLimits);
        foreach (var manifestPath in manifestPaths)
        {
            var discovered = await TryDiscoverPackAsync(
                sourcePath,
                sourceOrder,
                sourceName,
                sourceIdentity,
                manifestPath,
                effectiveResourceLimits
            );
            if (discovered.ErrorKind == ManifestOperationErrorKind.ResourceLimitExceeded)
            {
                return ManifestOperationResult<IReadOnlyList<CatalogPack>>.Failure(
                    discovered.Error ?? "Local pack manifest exceeds its configured byte limit.",
                    discovered.ErrorKind
                );
            }

            if (discovered.Value is { } pack)
            {
                var observed = manifestBudget.Observe(pack);
                if (!observed.IsSuccess)
                {
                    return ManifestOperationResult<IReadOnlyList<CatalogPack>>.Failure(
                        observed.Error!,
                        observed.ErrorKind
                    );
                }

                packs.Add(pack);
            }
        }

        return ManifestOperationResult<IReadOnlyList<CatalogPack>>.Success(packs);
    }

    public async Task<
        ManifestOperationResult<IReadOnlyList<LocalPackValidationResult>>
    > ValidateAsync(string sourcePath, ResourceLimits? resourceLimits = null)
    {
        var effectiveResourceLimits = resourceLimits ?? ResourceLimits.Default;
        var manifestPaths = EnumerateManifestPaths(
            sourcePath,
            effectiveResourceLimits.FilesPerGraph
        );
        if (manifestPaths.Value is not { } paths)
        {
            return ManifestOperationResult<IReadOnlyList<LocalPackValidationResult>>.Failure(
                manifestPaths.Error ?? "Unable to enumerate pack source."
            );
        }

        var results = new List<LocalPackValidationResult>(paths.Count);
        var manifestBudget = new CatalogManifestBudget(effectiveResourceLimits);
        foreach (var manifestPath in paths)
        {
            var validation = await ValidateManifestAsync(manifestPath, effectiveResourceLimits);
            if (validation.Value is not { } result)
            {
                return ManifestOperationResult<IReadOnlyList<LocalPackValidationResult>>.Failure(
                    validation.Error ?? "Unable to validate local pack manifest.",
                    validation.ErrorKind
                );
            }

            var observed = manifestBudget.Observe(result.ManifestByteCount);
            if (!observed.IsSuccess)
            {
                return ManifestOperationResult<IReadOnlyList<LocalPackValidationResult>>.Failure(
                    observed.Error!,
                    observed.ErrorKind
                );
            }

            results.Add(result);
        }

        return ManifestOperationResult<IReadOnlyList<LocalPackValidationResult>>.Success(results);
    }

    private async Task<ManifestOperationResult<CatalogPack?>> TryDiscoverPackAsync(
        string sourcePath,
        int sourceOrder,
        string sourceName,
        ConfiguredSourceIdentity sourceIdentity,
        string manifestPath,
        ResourceLimits resourceLimits
    )
    {
        var validationResult = await ValidateManifestAsync(manifestPath, resourceLimits);
        if (validationResult.Value is not { } validation)
        {
            return ManifestOperationResult<CatalogPack?>.Failure(
                validationResult.Error ?? "Unable to validate local pack manifest.",
                validationResult.ErrorKind
            );
        }

        if (
            !validation.IsValid
            || validation.Manifest is not { } pack
            || !NuGetVersion.TryParse(pack.Version, out var version)
        )
        {
            _console.Debug(
                $"Ignoring invalid pack manifest '{manifestPath}': {string.Join(" ", validation.Issues)}"
            );
            return ManifestOperationResult<CatalogPack?>.Success(null);
        }

        var packDirectory = _fileSystem.Path.GetDirectoryName(manifestPath);
        return ManifestOperationResult<CatalogPack?>.Success(
            packDirectory is null
                ? null
                : new CatalogPack(
                    sourcePath,
                    packDirectory,
                    sourceOrder,
                    pack,
                    version,
                    sourceName,
                    sourceIdentity,
                    ManifestByteCount: validation.ManifestByteCount
                )
        );
    }

    private ManifestOperationResult<List<string>> EnumerateManifestPaths(
        string sourcePath,
        long manifestLimit
    )
    {
        try
        {
            var manifestPaths = new List<string>();
            foreach (
                var manifestPath in _fileSystem.Directory.EnumerateFiles(
                    sourcePath,
                    "pack.yml",
                    SearchOption.AllDirectories
                )
            )
            {
                var observedPaths = manifestPaths.Count + 1;
                if (observedPaths > manifestLimit)
                {
                    return ManifestOperationResult<List<string>>.Failure(
                        $"Pack source '{sourcePath}' manifest enumeration exceeds its {manifestLimit} record limit (at least {observedPaths} pack manifests observed).",
                        ManifestOperationErrorKind.ResourceLimitExceeded
                    );
                }

                manifestPaths.Add(manifestPath);
            }

            manifestPaths.Sort(StringComparer.Ordinal);
            return ManifestOperationResult<List<string>>.Success(manifestPaths);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return ManifestOperationResult<List<string>>.Failure(
                $"Unable to enumerate pack source '{sourcePath}': {exception.Message}"
            );
        }
    }

    private async Task<ManifestOperationResult<LocalPackValidationResult>> ValidateManifestAsync(
        string manifestPath,
        ResourceLimits resourceLimits
    )
    {
        try
        {
            var read = await BoundedFileReader.ReadTextAsync(
                _fileSystem,
                manifestPath,
                resourceLimits.PackManifestBytes,
                "pack.yml"
            );
            if (read.Value is not { } contents)
            {
                return ManifestOperationResult<LocalPackValidationResult>.Failure(
                    read.Error ?? "Unable to read pack.yml.",
                    read.ErrorKind
                );
            }

            var manifest = _deserializer.Deserialize<PackManifest>(contents.Text);
            if (manifest is null)
            {
                return ManifestOperationResult<LocalPackValidationResult>.Success(
                    new LocalPackValidationResult(manifestPath, null, ["Pack manifest is empty."])
                );
            }

            manifest = PackManifestPathNormalizer.Normalize(manifest);

            var packDirectory = _fileSystem.Path.GetDirectoryName(manifestPath);
            if (packDirectory is null)
            {
                return ManifestOperationResult<LocalPackValidationResult>.Success(
                    new LocalPackValidationResult(
                        manifestPath,
                        manifest,
                        ["Pack manifest directory is unavailable."]
                    )
                );
            }

            var enumeratedFiles = PackSnapshotFileEnumerator.Enumerate(
                _fileSystem,
                packDirectory,
                manifest.Id,
                resourceLimits
            );
            if (enumeratedFiles.Value is not { } sourceFiles)
            {
                return ManifestOperationResult<LocalPackValidationResult>.Failure(
                    enumeratedFiles.Error ?? "Unable to enumerate pack snapshot.",
                    enumeratedFiles.ErrorKind
                );
            }

            return ManifestOperationResult<LocalPackValidationResult>.Success(
                new LocalPackValidationResult(
                    manifestPath,
                    manifest,
                    await PackManifestValidator.ValidateAsync(manifest, sourceFiles),
                    contents.ByteCount
                )
            );
        }
        catch (Exception exception)
            when (exception
                    is IOException
                        or UnauthorizedAccessException
                        or YamlDotNet.Core.YamlException
            )
        {
            return ManifestOperationResult<LocalPackValidationResult>.Success(
                new LocalPackValidationResult(manifestPath, null, [exception.Message])
            );
        }
    }
}
