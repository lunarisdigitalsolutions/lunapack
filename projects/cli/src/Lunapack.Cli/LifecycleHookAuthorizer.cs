namespace Lunapack.Cli;

[System.Diagnostics.CodeAnalysis.SuppressMessage("Maintainability", "MA0051:Method is too long")]
internal sealed class LifecycleHookAuthorizer(
    UserSettingsStore userSettingsStore,
    TrustPolicy trustPolicy,
    LifecycleCommandResolver commandResolver,
    ILifecycleHookConfirmer confirmer
)
{
    public async Task<
        ManifestOperationResult<IReadOnlyList<AuthorizedLifecycleHook>>
    > AuthorizeAsync(
        string projectDirectory,
        ProjectConfiguration configuration,
        ScriptExecutionMode scriptMode,
        IReadOnlyList<LifecycleHookInvocation> invocations
    )
    {
        var scripts = invocations.Where(static invocation => invocation.IsScript).ToArray();
        var authorization = await AuthorizeScriptsAsync(
            projectDirectory,
            configuration,
            scriptMode,
            scripts
        );
        if (authorization.Value is not { } authorizedScripts)
        {
            return ManifestOperationResult<IReadOnlyList<AuthorizedLifecycleHook>>.Failure(
                authorization.Error ?? "Unable to authorize lifecycle scripts."
            );
        }

        var resolvedByInvocation = authorizedScripts.ToDictionary(static script =>
            script.Invocation
        );
        var authorized = new List<AuthorizedLifecycleHook>(invocations.Count);
        foreach (var invocation in invocations)
        {
            if (invocation.IsInstruction)
            {
                authorized.Add(new AuthorizedLifecycleHook(invocation, null));
            }
            else if (resolvedByInvocation.TryGetValue(invocation, out var script))
            {
                authorized.Add(new AuthorizedLifecycleHook(invocation, script));
            }
        }

        return ManifestOperationResult<IReadOnlyList<AuthorizedLifecycleHook>>.Success(authorized);
    }

    private async Task<
        ManifestOperationResult<IReadOnlyList<ResolvedLifecycleHookInvocation>>
    > AuthorizeScriptsAsync(
        string projectDirectory,
        ProjectConfiguration configuration,
        ScriptExecutionMode scriptMode,
        IReadOnlyList<LifecycleHookInvocation> invocations
    )
    {
        if (scriptMode == ScriptExecutionMode.Skip)
        {
            return ManifestOperationResult<
                IReadOnlyList<ResolvedLifecycleHookInvocation>
            >.Success([]);
        }

        if (scriptMode == ScriptExecutionMode.Run)
        {
            return ResolveAll(invocations);
        }

        return await AuthorizePromptModeAsync(projectDirectory, configuration, invocations);
    }

    private async Task<
        ManifestOperationResult<IReadOnlyList<ResolvedLifecycleHookInvocation>>
    > AuthorizePromptModeAsync(
        string projectDirectory,
        ProjectConfiguration configuration,
        IReadOnlyList<LifecycleHookInvocation> invocations
    )
    {
        var context = await LoadPromptContextAsync(projectDirectory);
        if (context.Value is not { } trustContext)
        {
            return ManifestOperationResult<IReadOnlyList<ResolvedLifecycleHookInvocation>>.Failure(
                context.Error ?? "Unable to load lifecycle trust context."
            );
        }

        return AuthorizeInvocations(projectDirectory, configuration, trustContext, invocations);
    }

    private ManifestOperationResult<
        IReadOnlyList<ResolvedLifecycleHookInvocation>
    > AuthorizeInvocations(
        string projectDirectory,
        ProjectConfiguration configuration,
        TrustContext trustContext,
        IReadOnlyList<LifecycleHookInvocation> invocations
    )
    {
        var authorized = new List<ResolvedLifecycleHookInvocation>(invocations.Count);
        foreach (var invocation in invocations)
        {
            var authorization = AuthorizeInvocation(
                projectDirectory,
                configuration,
                trustContext,
                invocation
            );
            if (authorization.Value is not { } decision)
            {
                return ManifestOperationResult<
                    IReadOnlyList<ResolvedLifecycleHookInvocation>
                >.Failure(authorization.Error ?? "Lifecycle hook was not authorized.");
            }

            if (decision.Command is { } command)
            {
                authorized.Add(command);
            }
        }

        return ManifestOperationResult<IReadOnlyList<ResolvedLifecycleHookInvocation>>.Success(
            authorized
        );
    }

    private ManifestOperationResult<AuthorizationDecision> AuthorizeInvocation(
        string projectDirectory,
        ProjectConfiguration configuration,
        TrustContext trustContext,
        LifecycleHookInvocation invocation
    )
    {
        var resolved = commandResolver.Resolve(invocation);
        if (resolved.Value is not { } command)
        {
            return ManifestOperationResult<AuthorizationDecision>.Failure(
                resolved.Error ?? "Unable to resolve lifecycle hook command."
            );
        }

        return
            trustPolicy.IsTrusted(
                projectDirectory,
                trustContext.ProjectKey,
                configuration,
                trustContext.Settings,
                invocation.Pack.SourceName,
                invocation.Pack.SourceIdentity,
                invocation.Pack.Manifest.Id
            ) || confirmer.Confirm(command)
            ? ManifestOperationResult<AuthorizationDecision>.Success(new(command))
            : ManifestOperationResult<AuthorizationDecision>.Success(new(null));
    }

    private async Task<ManifestOperationResult<TrustContext>> LoadPromptContextAsync(
        string projectDirectory
    )
    {
        var settings = await userSettingsStore.LoadAsync();
        if (settings.Value is not { } userSettings)
        {
            return ManifestOperationResult<TrustContext>.Failure(
                settings.Error ?? "Unable to load lifecycle trust settings."
            );
        }

        var projectKey = userSettingsStore.GetProjectKey(projectDirectory);
        return projectKey.Value is { } key
            ? ManifestOperationResult<TrustContext>.Success(new TrustContext(userSettings, key))
            : ManifestOperationResult<TrustContext>.Failure(
                projectKey.Error ?? "Unable to resolve lifecycle trust project path."
            );
    }

    private ManifestOperationResult<IReadOnlyList<ResolvedLifecycleHookInvocation>> ResolveAll(
        IReadOnlyList<LifecycleHookInvocation> invocations
    )
    {
        var resolvedInvocations = new List<ResolvedLifecycleHookInvocation>(invocations.Count);
        foreach (var invocation in invocations)
        {
            var resolved = commandResolver.Resolve(invocation);
            if (resolved.Value is not { } command)
            {
                return ManifestOperationResult<
                    IReadOnlyList<ResolvedLifecycleHookInvocation>
                >.Failure(resolved.Error ?? "Unable to resolve lifecycle hook command.");
            }

            resolvedInvocations.Add(command);
        }

        return ManifestOperationResult<IReadOnlyList<ResolvedLifecycleHookInvocation>>.Success(
            resolvedInvocations
        );
    }

    private sealed record TrustContext(UserSettings Settings, string ProjectKey);

    private sealed record AuthorizationDecision(ResolvedLifecycleHookInvocation? Command);
}
