using System.CommandLine;
using System.CommandLine.Completions;
using Lunapack.Cli.Catalog;
using Lunapack.Cli.Project;

namespace Lunapack.Cli.Application;

internal sealed class CliCompletionProvider(
    CatalogService catalogService,
    IProjectStateStore projectStateStore,
    WorkspaceDirectoryResolver workspaceDirectoryResolver,
    string projectDirectory,
    Option<string?> workspaceOption
)
{
    public IEnumerable<CompletionItem> GetInstallReferences(CompletionContext context)
    {
        var state = LoadState(context);
        return CreateItems(
            GetCatalogPacks(context)
                .Select(pack => pack.Manifest.Id)
                .Concat(state?.Configuration.Links.Keys.AsEnumerable() ?? [])
        );
    }

    public IEnumerable<CompletionItem> GetAvailablePackIds(CompletionContext context) =>
        CreateItems(GetCatalogPacks(context).Select(pack => pack.Manifest.Id));

    public IEnumerable<CompletionItem> GetConfiguredLinkNames(CompletionContext context) =>
        CreateItems(LoadState(context)?.Configuration.Links.Keys.AsEnumerable() ?? []);

    public IEnumerable<CompletionItem> GetConfiguredSourceNames(CompletionContext context) =>
        CreateItems(LoadState(context)?.Configuration.Sources.Select(source => source.Name) ?? []);

    public IEnumerable<CompletionItem> GetConfiguredVariableNames(CompletionContext context) =>
        CreateItems(LoadState(context)?.Configuration.Variables.Keys.AsEnumerable() ?? []);

    public IEnumerable<CompletionItem> GetInstalledReferences(CompletionContext context)
    {
        var state = LoadState(context);
        return CreateItems(
            (state?.Configuration.Packs.Select(pack => pack.Id) ?? []).Concat(
                state?.LockFile.Links.Keys.AsEnumerable() ?? []
            )
        );
    }

    public IEnumerable<CompletionItem> GetPackIdsFromSelectedSource(
        CompletionContext context,
        Option<string?> sourceOption
    )
    {
        var sourceName = context.ParseResult.GetValue(sourceOption);
        return CreateItems(
            GetCatalogPacks(context)
                .Where(pack =>
                    sourceName is null
                    || string.Equals(pack.SourceName, sourceName, StringComparison.Ordinal)
                )
                .Select(pack => pack.Manifest.Id)
        );
    }

    private IReadOnlyList<CatalogPack> GetCatalogPacks(CompletionContext context)
    {
        var catalog = catalogService
            .LoadCachedAsync(ResolveWorkspace(context))
            .GetAwaiter()
            .GetResult();
        return catalog.Value ?? [];
    }

    private ProjectState? LoadState(CompletionContext context)
    {
        var state = projectStateStore.LoadAsync(ResolveWorkspace(context)).GetAwaiter().GetResult();
        return state.Value;
    }

    private string ResolveWorkspace(CompletionContext context) =>
        workspaceDirectoryResolver.Resolve(
            projectDirectory,
            context.ParseResult.GetValue(workspaceOption)
        );

    private static IEnumerable<CompletionItem> CreateItems(IEnumerable<string> values) =>
        values
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .Select(value => new CompletionItem(value));
}
