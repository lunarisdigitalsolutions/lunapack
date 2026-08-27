namespace Lunapack.Cli;

internal sealed class LinkCommandDispatcher(
    IProjectStateStore projectStateStore,
    LinkLifecycleService linkLifecycleService,
    INextStepAdvisor nextStepAdvisor,
    NextStepRenderer nextStepRenderer,
    CliConsole console
)
{
    public async Task<int?> TryInstallAsync(
        string projectDirectory,
        string name,
        bool adoptExisting,
        ManagedFileTargetRemapping targetRemapping
    )
    {
        if (!await IsConfiguredLinkAsync(projectDirectory, name))
        {
            return null;
        }

        TimeSpan? managedFileChangesDuration = null;
        var exitCode = await linkLifecycleService.InstallAsync(
            projectDirectory,
            name,
            adoptExisting,
            targetRemapping: targetRemapping,
            onManagedFileChangesApplied: duration => managedFileChangesDuration = duration
        );
        if (exitCode != 0)
        {
            return exitCode;
        }

        console.Success(
            $"✓ Installed link {name} in {CliDuration.Format(managedFileChangesDuration ?? TimeSpan.Zero)}"
        );
        nextStepRenderer.Render(nextStepAdvisor.Recommend(NextStepContext.LinkInstalled, name));
        return 0;
    }

    public async Task<int?> TryUpdateAsync(string projectDirectory, string name)
    {
        if (!await IsConfiguredLinkAsync(projectDirectory, name))
        {
            return null;
        }

        var exitCode = await linkLifecycleService.UpdateAsync(projectDirectory, name);
        if (exitCode == 0)
        {
            console.Success($"✓ Updated link {name}");
        }

        return exitCode;
    }

    public async Task<int?> TryUninstallAsync(string projectDirectory, string name)
    {
        var loadedState = await projectStateStore.LoadAsync(projectDirectory);
        if (loadedState.Value is not { } state || !state.LockFile.Links.ContainsKey(name))
        {
            return null;
        }

        var exitCode = await linkLifecycleService.UninstallAsync(projectDirectory, name);
        if (exitCode == 0)
        {
            console.Success($"✓ Uninstalled link {name}");
        }

        return exitCode;
    }

    private async Task<bool> IsConfiguredLinkAsync(string projectDirectory, string name)
    {
        var loadedState = await projectStateStore.LoadAsync(projectDirectory);
        return loadedState.Value is { } state
            && state.Configuration.Links.ContainsKey(name)
            && !state.LockFile.Packs.Exists(pack =>
                string.Equals(pack.Id, name, StringComparison.Ordinal)
            );
    }
}
