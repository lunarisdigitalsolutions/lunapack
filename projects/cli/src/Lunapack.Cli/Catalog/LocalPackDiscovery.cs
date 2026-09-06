using System.IO.Abstractions;
using Lunapack.Cli.Application;
using Lunapack.Cli.Application.CommandExecution;
using Lunapack.Cli.Application.Serialization;
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
        ConfiguredSourceIdentity sourceIdentity
    )
    {
        List<string> manifestPaths;
        try
        {
            manifestPaths =
            [
                .. _fileSystem
                    .Directory.EnumerateFiles(sourcePath, "pack.yml", SearchOption.AllDirectories)
                    .OrderBy(path => path, StringComparer.Ordinal),
            ];
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return ManifestOperationResult<IReadOnlyList<CatalogPack>>.Failure(
                $"Unable to enumerate pack source '{sourcePath}': {exception.Message}"
            );
        }

        var packs = new List<CatalogPack>(manifestPaths.Count);
        foreach (var manifestPath in manifestPaths)
        {
            var pack = await TryDiscoverPackAsync(
                sourcePath,
                sourceOrder,
                sourceName,
                sourceIdentity,
                manifestPath
            );
            if (pack is not null)
            {
                packs.Add(pack);
            }
        }

        return ManifestOperationResult<IReadOnlyList<CatalogPack>>.Success(packs);
    }

    public async Task<
        ManifestOperationResult<IReadOnlyList<LocalPackValidationResult>>
    > ValidateAsync(string sourcePath)
    {
        var manifestPaths = EnumerateManifestPaths(sourcePath);
        if (manifestPaths.Value is not { } paths)
        {
            return ManifestOperationResult<IReadOnlyList<LocalPackValidationResult>>.Failure(
                manifestPaths.Error ?? "Unable to enumerate pack source."
            );
        }

        var results = new List<LocalPackValidationResult>(paths.Count);
        foreach (var manifestPath in paths)
        {
            results.Add(await ValidateManifestAsync(manifestPath));
        }

        return ManifestOperationResult<IReadOnlyList<LocalPackValidationResult>>.Success(results);
    }

    private async Task<CatalogPack?> TryDiscoverPackAsync(
        string sourcePath,
        int sourceOrder,
        string sourceName,
        ConfiguredSourceIdentity sourceIdentity,
        string manifestPath
    )
    {
        var validation = await ValidateManifestAsync(manifestPath);
        if (
            !validation.IsValid
            || validation.Manifest is not { } pack
            || !NuGetVersion.TryParse(pack.Version, out var version)
        )
        {
            _console.Debug(
                $"Ignoring invalid pack manifest '{manifestPath}': {string.Join(" ", validation.Issues)}"
            );
            return null;
        }

        var packDirectory = _fileSystem.Path.GetDirectoryName(manifestPath);
        return packDirectory is null
            ? null
            : new CatalogPack(
                sourcePath,
                packDirectory,
                sourceOrder,
                pack,
                version,
                sourceName,
                sourceIdentity
            );
    }

    private ManifestOperationResult<List<string>> EnumerateManifestPaths(string sourcePath)
    {
        try
        {
            return ManifestOperationResult<List<string>>.Success([
                .. _fileSystem
                    .Directory.EnumerateFiles(sourcePath, "pack.yml", SearchOption.AllDirectories)
                    .OrderBy(path => path, StringComparer.Ordinal),
            ]);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return ManifestOperationResult<List<string>>.Failure(
                $"Unable to enumerate pack source '{sourcePath}': {exception.Message}"
            );
        }
    }

    private async Task<LocalPackValidationResult> ValidateManifestAsync(string manifestPath)
    {
        try
        {
            var manifest = _deserializer.Deserialize<PackManifest>(
                _fileSystem.File.ReadAllText(manifestPath)
            );
            if (manifest is null)
            {
                return new LocalPackValidationResult(
                    manifestPath,
                    null,
                    ["Pack manifest is empty."]
                );
            }

            manifest = PackManifestPathNormalizer.Normalize(manifest);

            var packDirectory = _fileSystem.Path.GetDirectoryName(manifestPath);
            if (packDirectory is null)
            {
                return new LocalPackValidationResult(
                    manifestPath,
                    manifest,
                    ["Pack manifest directory is unavailable."]
                );
            }

            var sourceFiles = _fileSystem
                .Directory.EnumerateFiles(packDirectory, "*", SearchOption.AllDirectories)
                .Select(path => _fileSystem.Path.GetRelativePath(packDirectory, path))
                .ToList();
            return new LocalPackValidationResult(
                manifestPath,
                manifest,
                await PackManifestValidator.ValidateAsync(manifest, sourceFiles)
            );
        }
        catch (Exception exception)
            when (exception
                    is IOException
                        or UnauthorizedAccessException
                        or YamlDotNet.Core.YamlException
            )
        {
            return new LocalPackValidationResult(manifestPath, null, [exception.Message]);
        }
    }
}
