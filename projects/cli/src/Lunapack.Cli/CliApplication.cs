using System.CommandLine;
using System.IO.Abstractions;
using Spectre.Console;

namespace Lunapack.Cli;

internal sealed class CliApplication(
    IFileSystem fileSystem,
    IAnsiConsole ansiConsole,
    IPackUpdatePrompter? packUpdatePrompter = null,
    ITrustConfirmer? trustConfirmer = null,
    UserSettingsStore? userSettingsStore = null,
    IGitProcessRunner? gitProcessRunner = null
)
{
    public async Task<int> RunAsync(
        string[] args,
        string projectDirectory,
        TextWriter? commandOutput = null
    )
    {
        if (!CliLogLevelParser.TryParse(args, out var minimumLevel, out var logLevelError))
        {
            return new CliConsole(ansiConsole, CliLogLevel.Info).Fail(logLevelError);
        }

        var console = new CliConsole(ansiConsole, minimumLevel);
        console.Debug($"Running CLI command in {projectDirectory}");
        var nextStepRenderer = new NextStepRenderer(console);
        var suppressNextStepsOption = CreateSuppressNextStepsOption();
        var rootCommand = CreateRootCommand(
            projectDirectory,
            console,
            nextStepRenderer,
            suppressNextStepsOption
        );
        var parseResult = rootCommand.Parse(args);
        nextStepRenderer.Suppress = parseResult.GetValue(suppressNextStepsOption);

        var exitCode = await parseResult.InvokeAsync(
            new InvocationConfiguration
            {
                Output = commandOutput ?? Console.Out,
                Error = Console.Error,
            }
        );

        console.Debug($"CLI command completed with exit code {exitCode}");
        return exitCode;
    }

    private RootCommand CreateRootCommand(
        string projectDirectory,
        CliConsole console,
        NextStepRenderer nextStepRenderer,
        Option<bool> suppressNextStepsOption
    )
    {
        var rootCommand = new RootCommand("Manage LunaPack packs.");
        var workspaceOption = CreateWorkspaceOption();
        var logLevelOption = CreateLogLevelOption();
        rootCommand.Options.Add(workspaceOption);
        rootCommand.Options.Add(logLevelOption);
        rootCommand.Options.Add(suppressNextStepsOption);
        var services = CreateCommandServices(console, nextStepRenderer);
        ConfigureRootAction(rootCommand, services, projectDirectory, workspaceOption, console);

        AddProjectCommands(
            rootCommand,
            fileSystem,
            services.ProjectStateStore,
            services.TrustService,
            services.WorkspaceDirectoryResolver,
            projectDirectory,
            workspaceOption,
            services.NextStepAdvisor,
            services.NextStepRenderer,
            console,
            services.GitRefResolver
        );
        AddCatalogCommands(
            rootCommand,
            services.CatalogService,
            services.PackValidationService,
            services.WorkspaceDirectoryResolver,
            projectDirectory,
            workspaceOption,
            services.NextStepAdvisor,
            services.NextStepRenderer,
            services.PrerequisiteGuard,
            console
        );
        AddPackAuthoringAndLifecycleCommands(
            rootCommand,
            services,
            projectDirectory,
            workspaceOption,
            console
        );
        AddAuditCommand(
            rootCommand,
            fileSystem,
            services.ProjectStateStore,
            services.WorkspaceDirectoryResolver,
            projectDirectory,
            workspaceOption,
            services.PrerequisiteGuard,
            console
        );

        return rootCommand;
    }

    private static void ConfigureRootAction(
        RootCommand rootCommand,
        CommandServices services,
        string projectDirectory,
        Option<string?> workspaceOption,
        CliConsole console
    ) =>
        rootCommand.SetAction(async parseResult =>
            await RenderWorkspaceGuidanceAsync(
                services.NextStepAdvisor,
                services.NextStepRenderer,
                services.WorkspaceDirectoryResolver.Resolve(
                    projectDirectory,
                    parseResult.GetValue(workspaceOption)
                ),
                console
            )
        );

    private CommandServices CreateCommandServices(
        CliConsole console,
        NextStepRenderer nextStepRenderer
    )
    {
        var projectStateStore = new ProjectStateStore(fileSystem);
        var effectiveUserSettingsStore = userSettingsStore ?? new UserSettingsStore(fileSystem);
        var workspaceDirectoryResolver = new WorkspaceDirectoryResolver(fileSystem);
        var effectiveGitProcessRunner = gitProcessRunner ?? new GitProcessRunner();
        var gitRefResolver = new GitRefResolver(effectiveGitProcessRunner);
        var packCatalog = new PackCatalog(fileSystem, console, effectiveGitProcessRunner);
        var nextStepAdvisor = new NextStepAdvisor(fileSystem, projectStateStore);
        var prerequisiteGuard = new WorkflowPrerequisiteGuard(
            nextStepAdvisor,
            nextStepRenderer,
            console
        );
        var lifecycleServices = CreateLifecycleServices(
            fileSystem,
            packCatalog,
            projectStateStore,
            console,
            effectiveGitProcessRunner,
            gitRefResolver
        );
        return new CommandServices(
            projectStateStore,
            workspaceDirectoryResolver,
            new CatalogService(packCatalog, projectStateStore),
            new PackValidationService(
                fileSystem,
                projectStateStore,
                new LocalPackDiscovery(fileSystem, console)
            ),
            lifecycleServices,
            packUpdatePrompter ?? new ConsolePackUpdatePrompter(console),
            nextStepAdvisor,
            nextStepRenderer,
            prerequisiteGuard,
            new TrustService(
                fileSystem,
                projectStateStore,
                effectiveUserSettingsStore,
                trustConfirmer ?? new ConsoleTrustConfirmer(console)
            ),
            gitRefResolver,
            effectiveGitProcessRunner
        );
    }

    private static void AddCatalogCommands(
        RootCommand rootCommand,
        CatalogService catalogService,
        PackValidationService packValidationService,
        WorkspaceDirectoryResolver workspaceDirectoryResolver,
        string projectDirectory,
        Option<string?> workspaceOption,
        INextStepAdvisor nextStepAdvisor,
        NextStepRenderer nextStepRenderer,
        WorkflowPrerequisiteGuard prerequisiteGuard,
        CliConsole console
    )
    {
        rootCommand.Add(
            new DiscoverPacksCommandHandler(
                catalogService,
                workspaceDirectoryResolver,
                nextStepAdvisor,
                nextStepRenderer,
                prerequisiteGuard,
                console
            ).CreateCommand(projectDirectory, workspaceOption)
        );
        rootCommand.Add(
            new SearchPacksCommandHandler(
                catalogService,
                workspaceDirectoryResolver,
                nextStepAdvisor,
                nextStepRenderer,
                prerequisiteGuard,
                console
            ).CreateCommand(projectDirectory, workspaceOption)
        );
        rootCommand.Add(
            new ValidatePackCommandHandler(
                packValidationService,
                workspaceDirectoryResolver,
                console
            ).CreateCommand(projectDirectory, workspaceOption)
        );
        rootCommand.Add(
            new InspectPackCommandHandler(
                catalogService,
                workspaceDirectoryResolver,
                nextStepAdvisor,
                nextStepRenderer,
                prerequisiteGuard,
                console
            ).CreateCommand(projectDirectory, workspaceOption)
        );
    }

    private static void AddPackAuthoringCommand(
        RootCommand rootCommand,
        IFileSystem fileSystem,
        WorkspaceDirectoryResolver workspaceDirectoryResolver,
        INextStepAdvisor nextStepAdvisor,
        NextStepRenderer nextStepRenderer,
        GitRefResolver gitRefResolver,
        IGitProcessRunner gitProcessRunner,
        string projectDirectory,
        Option<string?> workspaceOption,
        CliConsole console
    ) =>
        rootCommand.Add(
            new PackAuthoringCommandHandler(
                fileSystem,
                new PackManifestStore(fileSystem),
                workspaceDirectoryResolver,
                nextStepAdvisor,
                nextStepRenderer,
                console,
                gitRefResolver,
                new PackAuthoringValidationService(
                    new ExternalSourceRequirementPlanner(
                        gitRefResolver,
                        new ManagedFileConditionParser()
                    ),
                    new ExternalSourceMaterializer(fileSystem, gitProcessRunner, gitRefResolver),
                    new PackInstallationPlanner(
                        fileSystem,
                        new PackTemplateRenderer(fileSystem),
                        new ManagedFileConditionParser()
                    )
                )
            ).CreateCommand(projectDirectory, workspaceOption)
        );

    private void AddPackAuthoringAndLifecycleCommands(
        RootCommand rootCommand,
        CommandServices services,
        string projectDirectory,
        Option<string?> workspaceOption,
        CliConsole console
    )
    {
        AddPackAuthoringCommand(
            rootCommand,
            fileSystem,
            services.WorkspaceDirectoryResolver,
            services.NextStepAdvisor,
            services.NextStepRenderer,
            services.GitRefResolver,
            services.GitProcessRunner,
            projectDirectory,
            workspaceOption,
            console
        );
        AddLifecycleCommands(
            rootCommand,
            fileSystem,
            services.LifecycleServices.PackLifecycleService,
            services.LifecycleServices.PackUpdateService,
            services.LifecycleServices.PackUpdateSelectionService,
            services.PackUpdatePrompter,
            services.WorkspaceDirectoryResolver,
            projectDirectory,
            workspaceOption,
            services.NextStepAdvisor,
            services.NextStepRenderer,
            services.PrerequisiteGuard,
            console
        );
    }

    private static Option<string?> CreateWorkspaceOption() =>
        new("--workspace", "-w")
        {
            Description = "Directory to use as the workspace.",
            Recursive = true,
        };

    private static Option<string?> CreateLogLevelOption()
    {
        var option = new Option<string?>("--log-level", "-ll")
        {
            Description = "Minimum log level: verbose, debug, info, warning, or error.",
            Recursive = true,
        };
        option.CompletionSources.Add("verbose", "debug", "info", "warning", "error");
        return option;
    }

    private static Option<bool> CreateSuppressNextStepsOption() =>
        new("--suppress-next-steps")
        {
            Description = "Suppress contextual next-step recommendations.",
            Recursive = true,
        };

    private static LifecycleServices CreateLifecycleServices(
        IFileSystem fileSystem,
        PackCatalog packCatalog,
        ProjectStateStore projectStateStore,
        CliConsole console,
        IGitProcessRunner gitProcessRunner,
        GitRefResolver gitRefResolver
    )
    {
        var packLifecycleService = new PackLifecycleService(
            fileSystem,
            new CompositePackGraphResolver(packCatalog),
            new PackInstallationPlanner(
                fileSystem,
                new PackTemplateRenderer(fileSystem),
                new ManagedFileConditionParser()
            ),
            new PackUpdatePlanner(fileSystem),
            new PackUpdateTransaction(fileSystem, console),
            projectStateStore,
            console,
            new GitPackMaterializer(fileSystem, gitProcessRunner),
            configuredExternalSourceRequirementPlanner: new ExternalSourceRequirementPlanner(
                gitRefResolver,
                new ManagedFileConditionParser()
            ),
            configuredExternalSourceMaterializer: new ExternalSourceMaterializer(
                fileSystem,
                gitProcessRunner,
                gitRefResolver
            ),
            configuredExternalSourceConsentCoordinator: new ExternalSourceConsentCoordinator(
                console.IsInteractive
                    ? new ConsoleExternalSourceApprover(console)
                    : new DenyExternalSourceApprover(),
                console.IsInteractive
                    ? new ConsoleExternalSourceIdentifierPrompter(console)
                    : new DenyExternalSourceIdentifierPrompter()
            )
        );
        var updateSelectionService = new PackUpdateSelectionService(
            packCatalog,
            projectStateStore,
            packLifecycleService
        );
        return new LifecycleServices(
            packLifecycleService,
            new PackUpdateService(
                packCatalog,
                packLifecycleService,
                projectStateStore,
                new ConsoleSourceSwitchConfirmer(console)
            ),
            updateSelectionService
        );
    }

    private static void AddProjectCommands(
        RootCommand rootCommand,
        IFileSystem fileSystem,
        ProjectStateStore projectStateStore,
        TrustService trustService,
        WorkspaceDirectoryResolver workspaceDirectoryResolver,
        string projectDirectory,
        Option<string?> workspaceOption,
        INextStepAdvisor nextStepAdvisor,
        NextStepRenderer nextStepRenderer,
        CliConsole console,
        GitRefResolver gitRefResolver
    )
    {
        rootCommand.Add(
            new ProjectInitializationCommandHandler(
                fileSystem,
                projectStateStore,
                workspaceDirectoryResolver,
                nextStepAdvisor,
                nextStepRenderer,
                console
            ).CreateCommand(projectDirectory, workspaceOption)
        );
        rootCommand.Add(
            new LocalSourceCommandHandler(
                fileSystem,
                projectStateStore,
                workspaceDirectoryResolver,
                nextStepAdvisor,
                nextStepRenderer,
                console,
                gitRefResolver
            ).CreateCommand(projectDirectory, workspaceOption)
        );
        rootCommand.Add(
            new TrustCommandHandler(
                trustService,
                workspaceDirectoryResolver,
                console
            ).CreateCommand(projectDirectory, workspaceOption)
        );
        rootCommand.Add(
            new VariablesCommandHandler(
                projectStateStore,
                workspaceDirectoryResolver,
                console
            ).CreateCommand(projectDirectory, workspaceOption)
        );
        rootCommand.Add(
            new RemapCommandHandler(
                fileSystem,
                projectStateStore,
                workspaceDirectoryResolver,
                console
            ).CreateCommand(projectDirectory, workspaceOption)
        );
    }

    private static void AddLifecycleCommands(
        RootCommand rootCommand,
        IFileSystem fileSystem,
        PackLifecycleService packLifecycleService,
        PackUpdateService packUpdateService,
        PackUpdateSelectionService updateSelectionService,
        IPackUpdatePrompter packUpdatePrompter,
        WorkspaceDirectoryResolver workspaceDirectoryResolver,
        string projectDirectory,
        Option<string?> workspaceOption,
        INextStepAdvisor nextStepAdvisor,
        NextStepRenderer nextStepRenderer,
        WorkflowPrerequisiteGuard prerequisiteGuard,
        CliConsole console
    )
    {
        rootCommand.Add(
            new InstallPackCommandHandler(
                fileSystem,
                packLifecycleService,
                workspaceDirectoryResolver,
                nextStepAdvisor,
                nextStepRenderer,
                prerequisiteGuard,
                console
            ).CreateCommand(projectDirectory, workspaceOption)
        );
        rootCommand.Add(
            new UninstallPackCommandHandler(
                packLifecycleService,
                workspaceDirectoryResolver,
                nextStepAdvisor,
                nextStepRenderer,
                prerequisiteGuard,
                console
            ).CreateCommand(projectDirectory, workspaceOption)
        );
        rootCommand.Add(
            new MoveManagedFileCommandHandler(
                packLifecycleService,
                workspaceDirectoryResolver,
                console
            ).CreateCommand(projectDirectory, workspaceOption)
        );
        rootCommand.Add(
            new UpdatePackCommandHandler(
                packUpdateService,
                updateSelectionService,
                packUpdatePrompter,
                workspaceDirectoryResolver,
                nextStepAdvisor,
                nextStepRenderer,
                prerequisiteGuard,
                console
            ).CreateCommand(projectDirectory, workspaceOption)
        );
        rootCommand.Add(
            new OutdatedPackCommandHandler(
                updateSelectionService,
                workspaceDirectoryResolver,
                prerequisiteGuard,
                console
            ).CreateCommand(projectDirectory, workspaceOption)
        );
    }

    private static void AddAuditCommand(
        RootCommand rootCommand,
        IFileSystem fileSystem,
        ProjectStateStore projectStateStore,
        WorkspaceDirectoryResolver workspaceDirectoryResolver,
        string projectDirectory,
        Option<string?> workspaceOption,
        WorkflowPrerequisiteGuard prerequisiteGuard,
        CliConsole console
    ) =>
        rootCommand.Add(
            new AuditCommandHandler(
                new AuditService(fileSystem, projectStateStore),
                workspaceDirectoryResolver,
                prerequisiteGuard,
                console
            ).CreateCommand(projectDirectory, workspaceOption)
        );

    private sealed record LifecycleServices(
        PackLifecycleService PackLifecycleService,
        PackUpdateService PackUpdateService,
        PackUpdateSelectionService PackUpdateSelectionService
    );

    private sealed record CommandServices(
        ProjectStateStore ProjectStateStore,
        WorkspaceDirectoryResolver WorkspaceDirectoryResolver,
        CatalogService CatalogService,
        PackValidationService PackValidationService,
        LifecycleServices LifecycleServices,
        IPackUpdatePrompter PackUpdatePrompter,
        INextStepAdvisor NextStepAdvisor,
        NextStepRenderer NextStepRenderer,
        WorkflowPrerequisiteGuard PrerequisiteGuard,
        TrustService TrustService,
        GitRefResolver GitRefResolver,
        IGitProcessRunner GitProcessRunner
    );

    private static async Task<int> RenderWorkspaceGuidanceAsync(
        INextStepAdvisor nextStepAdvisor,
        NextStepRenderer nextStepRenderer,
        string projectDirectory,
        CliConsole console
    )
    {
        var inspectedWorkspace = await nextStepAdvisor.InspectWorkspaceAsync(projectDirectory);
        if (inspectedWorkspace.Value is not { } workspace)
        {
            return console.Fail(inspectedWorkspace.Error);
        }

        switch (workspace.Stage)
        {
            case WorkspaceStage.NoWorkspace:
                console.Info("No LunaPack workspace found.");
                nextStepRenderer.Render(workspace.Recommendations, "Get started with:");
                console.Info(string.Empty);
                console.Info("This creates:");
                console.Info(string.Empty);
                console.Info($"  {ProjectStateStore.ConfigurationFileName}");
                console.Info($"  {ProjectStateStore.LockFileName}");
                break;
            case WorkspaceStage.EmptyWorkspace:
                console.Info("Workspace detected.");
                console.Info(string.Empty);
                console.Info("No sources are configured.");
                nextStepRenderer.Render(workspace.Recommendations);
                break;
            case WorkspaceStage.SourcesConfigured:
            case WorkspaceStage.ActiveWorkspace:
                console.Info("Workspace detected.");
                console.Info(string.Empty);
                console.Info($"Configured sources: {workspace.SourceCount}");
                console.Info($"Installed packs: {workspace.InstalledPackCount}");
                nextStepRenderer.Render(workspace.Recommendations, "Suggested commands:");
                break;
            default:
                throw new InvalidOperationException(
                    $"Unsupported workspace stage '{workspace.Stage}'."
                );
        }

        return 0;
    }
}
