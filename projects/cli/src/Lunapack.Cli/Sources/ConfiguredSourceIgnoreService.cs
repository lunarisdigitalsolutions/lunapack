using System.IO.Abstractions;
using Lunapack.Cli.Application.CommandExecution;
using Lunapack.Cli.Project;
using Lunapack.Cli.Trust;

namespace Lunapack.Cli.Sources;

internal sealed class ConfiguredSourceIgnoreService(
    IFileSystem fileSystem,
    UserSettingsStore? userSettingsStore
)
{
    public async Task<ManifestOperationResult<SourceIgnoreContext>> LoadAsync(
        string projectDirectory
    )
    {
        if (userSettingsStore is null)
        {
            return ManifestOperationResult<SourceIgnoreContext>.Success(SourceIgnoreContext.Empty);
        }

        var settings = await userSettingsStore.LoadAsync();
        if (settings.Value is not { } userSettings)
        {
            return ManifestOperationResult<SourceIgnoreContext>.Failure(
                settings.Error ?? "Unable to load local source settings."
            );
        }

        var projectKey = userSettingsStore.GetProjectKey(projectDirectory);
        if (projectKey.Value is not { } canonicalProjectDirectory)
        {
            return ManifestOperationResult<SourceIgnoreContext>.Failure(
                projectKey.Error ?? "Unable to identify local source settings."
            );
        }

        var ignoredSources = userSettings.Projects.TryGetValue(
            canonicalProjectDirectory,
            out var projectSettings
        )
            ? projectSettings.IgnoredSources
            : [];
        return ManifestOperationResult<SourceIgnoreContext>.Success(
            new SourceIgnoreContext(
                canonicalProjectDirectory,
                new HashSet<ConfiguredSourceIdentity>(ignoredSources)
            )
        );
    }

    public ManifestOperationResult<bool> IsIgnored(
        SourceIgnoreContext context,
        ProjectConfiguration.Source source
    )
    {
        if (context.ProjectKey is null || context.IgnoredSources.Count == 0)
        {
            return ManifestOperationResult<bool>.Success(false);
        }

        var identity = ConfiguredSourceIdentity.CreateForIgnore(
            fileSystem,
            context.ProjectKey,
            source
        );
        return identity.Value is { } configuredIdentity
            ? ManifestOperationResult<bool>.Success(
                context.IgnoredSources.Contains(configuredIdentity)
            )
            : ManifestOperationResult<bool>.Failure(
                identity.Error ?? $"Unable to identify source '{source.Name}'."
            );
    }

    public async Task<ManifestOperationResult<string?>> FindIgnoredLockedPackErrorAsync(
        string projectDirectory,
        ProjectConfiguration configuration,
        IEnumerable<ProjectLockFile.ResolvedPack> lockedPacks
    )
    {
        var contextResult = await LoadAsync(projectDirectory);
        if (contextResult.Value is not { } context)
        {
            return ManifestOperationResult<string?>.Failure(
                contextResult.Error ?? "Unable to load local source ignore settings."
            );
        }

        foreach (var lockedPack in lockedPacks)
        {
            if (lockedPack.SourceIdentity is not { } identity)
            {
                continue;
            }

            var source = configuration.Sources.FirstOrDefault(candidate =>
                ConfiguredSourceIdentity.Create(candidate) == identity
            );
            if (source is null)
            {
                continue;
            }

            var ignored = IsIgnored(context, source);
            if (!ignored.IsSuccess)
            {
                return ManifestOperationResult<string?>.Failure(
                    ignored.Error ?? $"Unable to identify source '{source.Name}'."
                );
            }

            if (ignored.Value)
            {
                return ManifestOperationResult<string?>.Success(
                    $"Pack '{lockedPack.Id}@{lockedPack.Version}' requires source '{source.Name}', which is ignored locally. Run 'luna sources unignore {source.Name}' to continue."
                );
            }
        }

        return ManifestOperationResult<string?>.Success(null);
    }

    internal sealed record SourceIgnoreContext(
        string? ProjectKey,
        IReadOnlySet<ConfiguredSourceIdentity> IgnoredSources
    )
    {
        public static SourceIgnoreContext Empty { get; } =
            new(null, new HashSet<ConfiguredSourceIdentity>());
    }
}
