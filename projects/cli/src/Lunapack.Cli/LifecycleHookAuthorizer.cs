namespace Lunapack.Cli;

internal sealed class LifecycleHookAuthorizer(
    UserSettingsStore userSettingsStore,
    TrustPolicy trustPolicy,
    LifecycleCommandResolver commandResolver,
    ILifecycleHookConfirmer confirmer,
    ScriptPolicyEvaluator? configuredPolicyEvaluator = null
)
{
    private readonly ScriptPolicyEvaluator _policyEvaluator =
        configuredPolicyEvaluator ?? new ScriptPolicyEvaluator(userSettingsStore);

    public Task<ManifestOperationResult<ScriptPolicyEvaluation>> EvaluateScriptPolicyAsync(
        string projectDirectory,
        ProjectConfiguration configuration
    ) => _policyEvaluator.EvaluateAsync(projectDirectory, configuration);

    public async Task<
        ManifestOperationResult<IReadOnlyList<AuthorizedLifecycleHook>>
    > AuthorizeAsync(
        string projectDirectory,
        ProjectConfiguration configuration,
        ScriptExecutionMode scriptMode,
        IReadOnlyList<LifecycleHookInvocation> invocations
    )
    {
        var authorization = await AuthorizeWithDiagnosticsAsync(
            projectDirectory,
            configuration,
            scriptMode,
            invocations
        );
        return authorization.Value is { } value
            ? ManifestOperationResult<IReadOnlyList<AuthorizedLifecycleHook>>.Success(
                value.AuthorizedHooks
            )
            : ManifestOperationResult<IReadOnlyList<AuthorizedLifecycleHook>>.Failure(
                authorization.Error ?? "Unable to authorize lifecycle scripts."
            );
    }

    public async Task<
        ManifestOperationResult<LifecycleHookAuthorization>
    > AuthorizeWithDiagnosticsAsync(
        string projectDirectory,
        ProjectConfiguration configuration,
        ScriptExecutionMode scriptMode,
        IReadOnlyList<LifecycleHookInvocation> invocations
    )
    {
        var scripts = invocations.Where(static invocation => invocation.IsScript).ToArray();
        ScriptPolicyEvaluation? evaluation = null;
        if (scripts.Length > 0)
        {
            var policy = await _policyEvaluator.EvaluateAsync(projectDirectory, configuration);
            if (policy.Value is not { } value)
            {
                return ManifestOperationResult<LifecycleHookAuthorization>.Failure(
                    policy.Error ?? "Unable to evaluate lifecycle script policy."
                );
            }

            evaluation = value;
        }

        IReadOnlyList<PolicyDeniedLifecycleHook> deniedScripts = [];
        ManifestOperationResult<IReadOnlyList<ResolvedLifecycleHookInvocation>> authorization;
        if (evaluation?.IsDenied == true)
        {
            authorization = ManifestOperationResult<
                IReadOnlyList<ResolvedLifecycleHookInvocation>
            >.Success([]);
            deniedScripts = scripts
                .Select(script => new PolicyDeniedLifecycleHook(script, evaluation.DenyingScopes))
                .ToArray();
        }
        else
        {
            authorization = AuthorizeScripts(
                projectDirectory,
                configuration,
                evaluation,
                scriptMode,
                scripts
            );
        }

        if (authorization.Value is not { } authorizedScripts)
        {
            return ManifestOperationResult<LifecycleHookAuthorization>.Failure(
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

        return ManifestOperationResult<LifecycleHookAuthorization>.Success(
            new LifecycleHookAuthorization(authorized, deniedScripts)
        );
    }

    private ManifestOperationResult<
        IReadOnlyList<ResolvedLifecycleHookInvocation>
    > AuthorizeScripts(
        string projectDirectory,
        ProjectConfiguration configuration,
        ScriptPolicyEvaluation? evaluation,
        ScriptExecutionMode scriptMode,
        IReadOnlyList<LifecycleHookInvocation> invocations
    )
    {
        if (invocations.Count == 0)
        {
            return ManifestOperationResult<
                IReadOnlyList<ResolvedLifecycleHookInvocation>
            >.Success([]);
        }

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

        return AuthorizePromptMode(
            projectDirectory,
            configuration,
            evaluation
                ?? throw new InvalidOperationException("Script policy evaluation is required."),
            invocations
        );
    }

    private ManifestOperationResult<
        IReadOnlyList<ResolvedLifecycleHookInvocation>
    > AuthorizePromptMode(
        string projectDirectory,
        ProjectConfiguration configuration,
        ScriptPolicyEvaluation evaluation,
        IReadOnlyList<LifecycleHookInvocation> invocations
    ) => AuthorizeInvocations(projectDirectory, configuration, evaluation, invocations);

    private ManifestOperationResult<
        IReadOnlyList<ResolvedLifecycleHookInvocation>
    > AuthorizeInvocations(
        string projectDirectory,
        ProjectConfiguration configuration,
        ScriptPolicyEvaluation evaluation,
        IReadOnlyList<LifecycleHookInvocation> invocations
    )
    {
        var authorized = new List<ResolvedLifecycleHookInvocation>(invocations.Count);
        foreach (var invocation in invocations)
        {
            var authorization = AuthorizeInvocation(
                projectDirectory,
                configuration,
                evaluation,
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
        ScriptPolicyEvaluation evaluation,
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
                evaluation.ProjectKey,
                configuration,
                evaluation.Settings,
                invocation.Pack.SourceName,
                invocation.Pack.SourceIdentity,
                invocation.Pack.Manifest.Id
            ) || confirmer.Confirm(command)
            ? ManifestOperationResult<AuthorizationDecision>.Success(new(command))
            : ManifestOperationResult<AuthorizationDecision>.Success(new(null));
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

    private sealed record AuthorizationDecision(ResolvedLifecycleHookInvocation? Command);
}
