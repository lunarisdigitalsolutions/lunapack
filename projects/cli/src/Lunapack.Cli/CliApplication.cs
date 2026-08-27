using System.CommandLine;
using System.IO.Abstractions;
using Spectre.Console;

namespace Lunapack.Cli;

internal sealed class CliApplication(
    IFileSystem fileSystem,
    IAnsiConsole ansiConsole,
    IPackUpdatePrompter? packUpdatePrompter = null,
    ITrustConfirmer? trustConfirmer = null,
    UserSettingsStore? userSettingsStore = null
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

        var invocationConfiguration = new InvocationConfiguration
        {
            Output = commandOutput ?? Console.Out,
            Error = Console.Error,
        };
        if (args.Length == 0)
        {
            await rootCommand.Parse(["--help"]).InvokeAsync(invocationConfiguration);
        }

        var exitCode = await parseResult.InvokeAsync(invocationConfiguration);

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
            console
        );
        AddCatalogCommands(
            rootCommand,
            services.CatalogService,
            services.PackValidationService,
            services.LinkServices.LinkInspectionService,
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
            services.ProjectStateStore,
            services.LinkServices.LinkLifecycleService,
            services.WorkspaceDirectoryResolver,
            projectDirectory,
            workspaceOption,
            services.PrerequisiteGuard,
            console
        );
        AddLinksCommand(rootCommand, services, projectDirectory, workspaceOption, console);

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
        var packCatalog = new PackCatalog(fileSystem, console);
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
            console
        );
        var linkServices = CreateLinkServices(fileSystem, projectStateStore, console);
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
            linkServices,
            packUpdatePrompter ?? new ConsolePackUpdatePrompter(console),
            nextStepAdvisor,
            nextStepRenderer,
            prerequisiteGuard,
            new TrustService(
                fileSystem,
                projectStateStore,
                effectiveUserSettingsStore,
                trustConfirmer ?? new ConsoleTrustConfirmer(console)
            )
        );
    }

    private static void AddCatalogCommands(
        RootCommand rootCommand,
        CatalogService catalogService,
        PackValidationService packValidationService,
        LinkInspectionService linkInspectionService,
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
                linkInspectionService,
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
                console
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
            projectDirectory,
            workspaceOption,
            console
        );
        AddLifecycleCommands(
            rootCommand,
            fileSystem,
            services.LifecycleServices.PackLifecycleService,
            services.LinkServices.CreateDispatcher(
                services.ProjectStateStore,
                services.NextStepAdvisor,
                services.NextStepRenderer,
                console
            ),
            services.LinkServices.LinkLifecycleService,
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
        CliConsole console
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
            console
        );
        var updateSelectionService = new PackUpdateSelectionService(packCatalog, projectStateStore);
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
        CliConsole console
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
                console
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
        LinkCommandDispatcher linkCommandDispatcher,
        LinkLifecycleService linkLifecycleService,
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
                linkCommandDispatcher,
                workspaceDirectoryResolver,
                nextStepAdvisor,
                nextStepRenderer,
                prerequisiteGuard,
                console
            ).CreateCommand(projectDirectory, workspaceOption)
        );
        rootCommand.Add(
            new UninstallPackCommandHandler(
                fileSystem,
                packLifecycleService,
                linkCommandDispatcher,
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
                linkCommandDispatcher,
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
                linkLifecycleService,
                workspaceDirectoryResolver,
                prerequisiteGuard,
                console
            ).CreateCommand(projectDirectory, workspaceOption)
        );
    }

    private static void AddLinksCommand(
        RootCommand rootCommand,
        CommandServices services,
        string projectDirectory,
        Option<string?> workspaceOption,
        CliConsole console
    ) =>
        rootCommand.Add(
            new LinksCommandHandler(
                services.ProjectStateStore,
                services.LinkServices.LinkDefinitionFactory,
                services.LinkServices.LinkLifecycleService,
                services.LinkServices.LinkInspectionService,
                services.WorkspaceDirectoryResolver,
                services.NextStepAdvisor,
                services.NextStepRenderer,
                console
            ).CreateCommand(projectDirectory, workspaceOption)
        );

    private static void AddAuditCommand(
        RootCommand rootCommand,
        ProjectStateStore projectStateStore,
        LinkLifecycleService linkLifecycleService,
        WorkspaceDirectoryResolver workspaceDirectoryResolver,
        string projectDirectory,
        Option<string?> workspaceOption,
        WorkflowPrerequisiteGuard prerequisiteGuard,
        CliConsole console
    ) =>
        rootCommand.Add(
            new AuditCommandHandler(
                projectStateStore,
                linkLifecycleService,
                workspaceDirectoryResolver,
                prerequisiteGuard,
                console
            ).CreateCommand(projectDirectory, workspaceOption)
        );

    private static LinkServices CreateLinkServices(
        IFileSystem fileSystem,
        ProjectStateStore projectStateStore,
        CliConsole console
    )
    {
        var gitProcessRunner = new GitProcessRunner();
        var linkResolver = new LinkResolver(
            fileSystem,
            new LinkTargetMapper(fileSystem),
            [
                new LocalLinkSourceProvider(fileSystem),
                new GitLinkSourceProvider(
                    fileSystem,
                    gitProcessRunner,
                    new GitRefResolver(gitProcessRunner),
                    new GitLinkCache(fileSystem, LinkSourceCacheRoot.Resolve(fileSystem))
                ),
            ]
        );
        return new LinkServices(
            new LinkDefinitionFactory(fileSystem),
            new LinkLifecycleService(
                fileSystem,
                linkResolver,
                new LinkPlanner(fileSystem),
                new PackUpdateTransaction(fileSystem, console),
                projectStateStore,
                console
            ),
            new LinkInspectionService(fileSystem, projectStateStore)
        );
    }

    private sealed record LinkServices(
        LinkDefinitionFactory LinkDefinitionFactory,
        LinkLifecycleService LinkLifecycleService,
        LinkInspectionService LinkInspectionService
    )
    {
        public LinkCommandDispatcher CreateDispatcher(
            IProjectStateStore projectStateStore,
            INextStepAdvisor nextStepAdvisor,
            NextStepRenderer nextStepRenderer,
            CliConsole console
        ) =>
            new(
                projectStateStore,
                LinkLifecycleService,
                nextStepAdvisor,
                nextStepRenderer,
                console
            );
    }

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
        LinkServices LinkServices,
        IPackUpdatePrompter PackUpdatePrompter,
        INextStepAdvisor NextStepAdvisor,
        NextStepRenderer NextStepRenderer,
        WorkflowPrerequisiteGuard PrerequisiteGuard,
        TrustService TrustService
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
