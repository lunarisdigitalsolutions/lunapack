using System.CommandLine;
using System.IO.Abstractions;
using Lunapack.Cli.Application;
using Lunapack.Cli.Application.Completions;
using Lunapack.Cli.Application.Guidance;
using Lunapack.Cli.Audit;
using Lunapack.Cli.Catalog;
using Lunapack.Cli.Catalog.Commands;
using Lunapack.Cli.Links;
using Lunapack.Cli.Links.Commands;
using Lunapack.Cli.Packs;
using Lunapack.Cli.Packs.Authoring;
using Lunapack.Cli.Packs.Commands;
using Lunapack.Cli.Packs.ExternalSources;
using Lunapack.Cli.Packs.Lifecycle;
using Lunapack.Cli.Packs.Manifest;
using Lunapack.Cli.Packs.Planning;
using Lunapack.Cli.Packs.Validation;
using Lunapack.Cli.Project;
using Lunapack.Cli.Project.Commands;
using Lunapack.Cli.Sources.Commands;
using Lunapack.Cli.Sources.Git;
using Lunapack.Cli.Trust;
using Spectre.Console;

namespace Lunapack.Cli;

internal sealed class CliApplication(
    IFileSystem fileSystem,
    IAnsiConsole ansiConsole,
    IPackUpdatePrompter? packUpdatePrompter = null,
    ITrustConfirmer? trustConfirmer = null,
    UserSettingsStore? userSettingsStore = null,
    IGitProcessRunner? gitProcessRunner = null,
    CompletionScriptInstallerResolver? completionScriptInstallerResolver = null
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
        var completionProvider = new CliCompletionProvider(
            services.CatalogService,
            services.ProjectStateStore,
            services.WorkspaceDirectoryResolver,
            projectDirectory,
            workspaceOption
        );
        ConfigureRootAction(rootCommand, services, projectDirectory, workspaceOption, console);

        AddProjectCommands(
            rootCommand,
            fileSystem,
            services.ProjectStateStore,
            services.TrustService,
            completionProvider,
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
            services.LinkServices.LinkInspectionService,
            completionProvider,
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
            completionProvider,
            projectDirectory,
            workspaceOption,
            console
        );
        AddAuditCommand(
            rootCommand,
            fileSystem,
            services.ProjectStateStore,
            services.LinkServices.LinkLifecycleService,
            services.WorkspaceDirectoryResolver,
            projectDirectory,
            workspaceOption,
            services.PrerequisiteGuard,
            console
        );
        AddLinksCommand(
            rootCommand,
            services,
            completionProvider,
            projectDirectory,
            workspaceOption,
            console
        );
        AddCompletionCommands(rootCommand, console);

        return rootCommand;
    }

    private void AddCompletionCommands(RootCommand rootCommand, CliConsole console)
    {
        var handler = new CompletionCommandHandler(
            rootCommand,
            console,
            CreateCompletionScriptInstallerResolver()
        );
        rootCommand.Add(handler.CreateCompleteCommand());
        rootCommand.Add(handler.CreateCompletionsCommand());
    }

    private CompletionScriptInstallerResolver CreateCompletionScriptInstallerResolver()
    {
        if (completionScriptInstallerResolver is not null)
        {
            return completionScriptInstallerResolver;
        }

        var userProfileDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var isWindows = OperatingSystem.IsWindows();
        return new CompletionScriptInstallerResolver([
            new BashCompletionScriptInstaller(fileSystem, userProfileDirectory),
            new FishCompletionScriptInstaller(fileSystem, userProfileDirectory),
            new NushellCompletionScriptInstaller(
                fileSystem,
                userProfileDirectory,
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                isWindows
            ),
            new PowerShellCompletionScriptInstaller(
                fileSystem,
                userProfileDirectory,
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                isWindows
            ),
            new ZshCompletionScriptInstaller(fileSystem, userProfileDirectory),
        ]);
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
            effectiveUserSettingsStore,
            console,
            effectiveGitProcessRunner,
            gitRefResolver
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
            ),
            gitRefResolver,
            effectiveGitProcessRunner
        );
    }

    private static void AddCatalogCommands(
        RootCommand rootCommand,
        CatalogService catalogService,
        PackValidationService packValidationService,
        LinkInspectionService linkInspectionService,
        CliCompletionProvider completionProvider,
        WorkspaceDirectoryResolver workspaceDirectoryResolver,
        string projectDirectory,
        Option<string?> workspaceOption,
        NextStepAdvisor nextStepAdvisor,
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
                completionProvider,
                workspaceDirectoryResolver,
                console
            ).CreateCommand(projectDirectory, workspaceOption)
        );
        rootCommand.Add(
            new InspectPackCommandHandler(
                catalogService,
                completionProvider,
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
        NextStepAdvisor nextStepAdvisor,
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
                    new ExternalSourceRequirementPlanner(gitRefResolver),
                    new ExternalSourceMaterializer(fileSystem, gitProcessRunner, gitRefResolver),
                    new PackInstallationPlanner(fileSystem, new PackTemplateRenderer(fileSystem))
                )
            ).CreateCommand(projectDirectory, workspaceOption)
        );

    private void AddPackAuthoringAndLifecycleCommands(
        RootCommand rootCommand,
        CommandServices services,
        CliCompletionProvider completionProvider,
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
            services.LinkServices.CreateDispatcher(
                services.ProjectStateStore,
                services.NextStepAdvisor,
                services.NextStepRenderer,
                console
            ),
            completionProvider,
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
        UserSettingsStore userSettingsStore,
        CliConsole console,
        IGitProcessRunner gitProcessRunner,
        GitRefResolver gitRefResolver
    )
    {
        var packLifecycleService = new PackLifecycleService(
            fileSystem,
            new CompositePackGraphResolver(packCatalog),
            new PackInstallationPlanner(fileSystem, new PackTemplateRenderer(fileSystem)),
            new PackUpdatePlanner(fileSystem),
            new PackUpdateTransaction(fileSystem, console),
            projectStateStore,
            console,
            new GitPackMaterializer(fileSystem, gitProcessRunner, console),
            configuredHookAuthorizer: new LifecycleHookAuthorizer(
                userSettingsStore,
                new TrustPolicy(fileSystem),
                new LifecycleCommandResolver(fileSystem),
                new ConsoleLifecycleHookConfirmer(console)
            ),
            configuredExternalSourceRequirementPlanner: new ExternalSourceRequirementPlanner(
                gitRefResolver
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
        CliCompletionProvider completionProvider,
        WorkspaceDirectoryResolver workspaceDirectoryResolver,
        string projectDirectory,
        Option<string?> workspaceOption,
        NextStepAdvisor nextStepAdvisor,
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
                gitRefResolver,
                completionProvider
            ).CreateCommand(projectDirectory, workspaceOption)
        );
        rootCommand.Add(
            new TrustCommandHandler(
                trustService,
                completionProvider,
                workspaceDirectoryResolver,
                console
            ).CreateCommand(projectDirectory, workspaceOption)
        );
        rootCommand.Add(
            new VariablesCommandHandler(
                projectStateStore,
                completionProvider,
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
        CliCompletionProvider completionProvider,
        LinkLifecycleService linkLifecycleService,
        PackUpdateService packUpdateService,
        PackUpdateSelectionService updateSelectionService,
        IPackUpdatePrompter packUpdatePrompter,
        WorkspaceDirectoryResolver workspaceDirectoryResolver,
        string projectDirectory,
        Option<string?> workspaceOption,
        NextStepAdvisor nextStepAdvisor,
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
                completionProvider,
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
                completionProvider,
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
                completionProvider,
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
        CliCompletionProvider completionProvider,
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
                completionProvider,
                services.WorkspaceDirectoryResolver,
                services.NextStepAdvisor,
                services.NextStepRenderer,
                console
            ).CreateCommand(projectDirectory, workspaceOption)
        );

    private static void AddAuditCommand(
        RootCommand rootCommand,
        IFileSystem fileSystem,
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
                new AuditService(fileSystem, projectStateStore),
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
            NextStepAdvisor nextStepAdvisor,
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
        NextStepAdvisor NextStepAdvisor,
        NextStepRenderer NextStepRenderer,
        WorkflowPrerequisiteGuard PrerequisiteGuard,
        TrustService TrustService,
        GitRefResolver GitRefResolver,
        IGitProcessRunner GitProcessRunner
    );

    private static async Task<int> RenderWorkspaceGuidanceAsync(
        NextStepAdvisor nextStepAdvisor,
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
