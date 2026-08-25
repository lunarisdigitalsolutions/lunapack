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
        var rootCommand = CreateRootCommand(projectDirectory, console);

        var exitCode = await rootCommand
            .Parse(args)
            .InvokeAsync(
                new InvocationConfiguration
                {
                    Output = commandOutput ?? Console.Out,
                    Error = Console.Error,
                }
            );

        console.Debug($"CLI command completed with exit code {exitCode}");
        return exitCode;
    }

    private RootCommand CreateRootCommand(string projectDirectory, CliConsole console)
    {
        var rootCommand = new RootCommand("Manage LunaPack packs.");
        var workspaceOption = CreateWorkspaceOption();
        var logLevelOption = CreateLogLevelOption();
        rootCommand.Options.Add(workspaceOption);
        rootCommand.Options.Add(logLevelOption);
        var services = CreateCommandServices(console);

        AddProjectCommands(
            rootCommand,
            fileSystem,
            services.ProjectStateStore,
            services.TrustService,
            services.WorkspaceDirectoryResolver,
            projectDirectory,
            workspaceOption,
            console
        );
        AddCatalogCommands(
            rootCommand,
            services.CatalogService,
            services.PackValidationService,
            services.WorkspaceDirectoryResolver,
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
            console
        );
        AddAuditCommand(
            rootCommand,
            services.ProjectStateStore,
            services.WorkspaceDirectoryResolver,
            projectDirectory,
            workspaceOption,
            console
        );

        return rootCommand;
    }

    private CommandServices CreateCommandServices(CliConsole console)
    {
        var projectStateStore = new ProjectStateStore(fileSystem);
        var effectiveUserSettingsStore = userSettingsStore ?? new UserSettingsStore(fileSystem);
        var workspaceDirectoryResolver = new WorkspaceDirectoryResolver(fileSystem);
        var packCatalog = new PackCatalog(fileSystem, console);
        var lifecycleServices = CreateLifecycleServices(
            fileSystem,
            packCatalog,
            projectStateStore,
            console
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
        WorkspaceDirectoryResolver workspaceDirectoryResolver,
        string projectDirectory,
        Option<string?> workspaceOption,
        CliConsole console
    )
    {
        rootCommand.Add(
            new DiscoverPacksCommandHandler(
                catalogService,
                workspaceDirectoryResolver,
                console
            ).CreateCommand(projectDirectory, workspaceOption)
        );
        rootCommand.Add(
            new SearchPacksCommandHandler(
                catalogService,
                workspaceDirectoryResolver,
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
                console
            ).CreateCommand(projectDirectory, workspaceOption)
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
        CliConsole console
    )
    {
        rootCommand.Add(
            new ProjectInitializationCommandHandler(
                fileSystem,
                projectStateStore,
                workspaceDirectoryResolver,
                console
            ).CreateCommand(projectDirectory, workspaceOption)
        );
        rootCommand.Add(
            new LocalSourceCommandHandler(
                fileSystem,
                projectStateStore,
                workspaceDirectoryResolver,
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
        PackUpdateService packUpdateService,
        PackUpdateSelectionService updateSelectionService,
        IPackUpdatePrompter packUpdatePrompter,
        WorkspaceDirectoryResolver workspaceDirectoryResolver,
        string projectDirectory,
        Option<string?> workspaceOption,
        CliConsole console
    )
    {
        rootCommand.Add(
            new InstallPackCommandHandler(
                fileSystem,
                packLifecycleService,
                workspaceDirectoryResolver,
                console
            ).CreateCommand(projectDirectory, workspaceOption)
        );
        rootCommand.Add(
            new UninstallPackCommandHandler(
                packLifecycleService,
                workspaceDirectoryResolver,
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
                console
            ).CreateCommand(projectDirectory, workspaceOption)
        );
        rootCommand.Add(
            new OutdatedPackCommandHandler(
                updateSelectionService,
                workspaceDirectoryResolver,
                console
            ).CreateCommand(projectDirectory, workspaceOption)
        );
    }

    private static void AddAuditCommand(
        RootCommand rootCommand,
        ProjectStateStore projectStateStore,
        WorkspaceDirectoryResolver workspaceDirectoryResolver,
        string projectDirectory,
        Option<string?> workspaceOption,
        CliConsole console
    ) =>
        rootCommand.Add(
            new AuditCommandHandler(
                projectStateStore,
                workspaceDirectoryResolver,
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
        TrustService TrustService
    );
}
