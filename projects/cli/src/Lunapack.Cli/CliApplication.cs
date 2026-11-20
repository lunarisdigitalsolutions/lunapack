using System.CommandLine;
using System.IO.Abstractions;
using Lunapack.Cli.Application;
using Lunapack.Cli.Application.Completions;
using Lunapack.Cli.Application.Diagnostics;
using Lunapack.Cli.Application.Guidance;
using Lunapack.Cli.Application.Preferences;
using Lunapack.Cli.Audit;
using Lunapack.Cli.Catalog;
using Lunapack.Cli.Catalog.Commands;
using Lunapack.Cli.Doctor;
using Lunapack.Cli.Links;
using Lunapack.Cli.Links.Commands;
using Lunapack.Cli.Packs;
using Lunapack.Cli.Packs.Authoring;
using Lunapack.Cli.Packs.Commands;
using Lunapack.Cli.Packs.ExternalSources;
using Lunapack.Cli.Packs.Lifecycle;
using Lunapack.Cli.Packs.Manifest;
using Lunapack.Cli.Packs.Planning;
using Lunapack.Cli.Packs.Publishing;
using Lunapack.Cli.Packs.Validation;
using Lunapack.Cli.Project;
using Lunapack.Cli.Project.Commands;
using Lunapack.Cli.Sbom;
using Lunapack.Cli.Sources;
using Lunapack.Cli.Sources.Commands;
using Lunapack.Cli.Sources.Git;
using Lunapack.Cli.Sources.Oci;
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
    CompletionScriptInstallerResolver? completionScriptInstallerResolver = null,
    IDoctorService? doctorService = null
)
{
    public async Task<int> RunAsync(
        string[] args,
        string projectDirectory,
        TextWriter? commandOutput = null,
        TextWriter? commandError = null
    )
    {
        var streams = CliInvocationStreams.Create(args, ansiConsole, commandOutput, commandError);
        var formatError = ValidateOutputFormat(args, streams);
        if (formatError is { } formatExitCode)
        {
            return formatExitCode;
        }

        var bootstrap = CreateInvocationBootstrap(args, streams);
        if (bootstrap.Console is not { } console)
        {
            return bootstrap.ExitCode;
        }

        DebugUnlessSbom(
            console,
            streams,
            $"Running CLI command in {DoctorText.Sanitize(projectDirectory)}"
        );
        var invocationConfiguration = CreateInvocationConfiguration(
            streams.CommandOutput,
            streams.DiagnosticOutput
        );
        var doctorExitCode = await TryRunDoctorAsync(
            args,
            projectDirectory,
            console,
            invocationConfiguration
        );
        if (doctorExitCode is not null)
        {
            return doctorExitCode.Value;
        }

        var nextStepRenderer = new NextStepRenderer(console);
        using var services = CreateCommandServices(console, nextStepRenderer);
        var suppressNextStepsOption = CreateSuppressNextStepsOption();
        var workspaceOption = CreateWorkspaceOption();
        var rootCommand = CreateRootCommand(
            projectDirectory,
            console,
            suppressNextStepsOption,
            workspaceOption,
            services,
            streams.CommandOutput,
            streams.DiagnosticOutput
        );
        var parseResult = rootCommand.Parse(args);
        var earlyExitCode = await InvokeParseErrorOrHelpAsync(args, parseResult, streams);
        if (earlyExitCode is { } parseOrHelpExitCode)
        {
            return parseOrHelpExitCode;
        }

        var preferenceExitCode = await ApplyPreferencesAndReportAsync(
            parseResult,
            projectDirectory,
            workspaceOption,
            suppressNextStepsOption,
            services,
            nextStepRenderer,
            console,
            streams
        );
        if (preferenceExitCode is { } exitCode)
        {
            return exitCode;
        }

        if (args.Length == 0)
        {
            await rootCommand.Parse(["--help"]).InvokeAsync(invocationConfiguration);
        }

        var commandExitCode = await parseResult.InvokeAsync(invocationConfiguration);
        var completionMessage = $"CLI command completed with exit code {commandExitCode}";
        DebugUnlessSbom(console, streams, completionMessage);

        return commandExitCode;
    }

    private static InvocationBootstrap CreateInvocationBootstrap(
        string[] args,
        CliInvocationStreams streams
    )
    {
        if (CliLogLevelParser.TryParse(args, out var minimumLevel, out var logLevelError))
        {
            return new InvocationBootstrap(
                new CliConsole(
                    streams.DiagnosticConsole,
                    minimumLevel,
                    sanitizeOutput: streams.RoutesDiagnosticsToError
                ),
                0
            );
        }

        var exitCode = new CliConsole(
            streams.DiagnosticConsole,
            CliLogLevel.Info,
            sanitizeOutput: streams.RoutesDiagnosticsToError
        ).Fail(logLevelError);
        if (streams.JsonCommand is { } jsonCommand)
        {
            CliJsonWriter.WriteFailure(streams.CommandOutput, jsonCommand);
        }

        return new InvocationBootstrap(null, exitCode);
    }

    private static int? ValidateOutputFormat(
        IReadOnlyList<string> args,
        CliInvocationStreams streams
    )
    {
        if (
            !CliOutputFormatOption.TryGetSupportedCommand(args, out _)
            || !CliOutputFormatOption.TryGetFormat(args, out var requestedFormat)
            || (
                requestedFormat is not null
                && CliOutputFormatOption.TryParse(requestedFormat, out _)
            )
        )
        {
            return null;
        }

        return new CliConsole(
            streams.DiagnosticConsole,
            CliLogLevel.Info,
            sanitizeOutput: true
        ).Fail(CliOutputFormatOption.FormatUnsupportedMessage(requestedFormat));
    }

    private static void DebugUnlessSbom(
        CliConsole console,
        CliInvocationStreams streams,
        string message
    )
    {
        if (!streams.IsSbomCommand)
        {
            console.Debug(message);
        }
    }

    private static async Task<int?> InvokeParseErrorOrHelpAsync(
        IReadOnlyList<string> args,
        ParseResult parseResult,
        CliInvocationStreams streams
    )
    {
        var isHelpRequest = args.Any(argument =>
            string.Equals(argument, "--help", StringComparison.Ordinal)
            || string.Equals(argument, "-h", StringComparison.Ordinal)
        );
        if (!streams.RoutesDiagnosticsToError || (parseResult.Errors.Count == 0 && !isHelpRequest))
        {
            return null;
        }

        var helpOutput =
            streams.IsSbomCommand && isHelpRequest ? streams.CommandOutput : streams.RawError;
        var invocationConfiguration = new InvocationConfiguration
        {
            Output = helpOutput,
            Error = streams.DiagnosticOutput,
        };
        var exitCode = await parseResult.InvokeAsync(invocationConfiguration);
        if (parseResult.Errors.Count > 0 && streams.JsonCommand is { } jsonCommand)
        {
            CliJsonWriter.WriteFailure(streams.CommandOutput, jsonCommand);
        }

        return exitCode;
    }

    private static InvocationConfiguration CreateInvocationConfiguration(
        TextWriter output,
        TextWriter error
    ) => new() { Output = output, Error = error };

    private static async Task<int?> ApplyPreferencesAndReportAsync(
        ParseResult parseResult,
        string projectDirectory,
        Option<string?> workspaceOption,
        Option<bool> suppressNextStepsOption,
        CommandServices services,
        NextStepRenderer nextStepRenderer,
        CliConsole console,
        CliInvocationStreams streams
    )
    {
        if (streams.IsSbomCommand)
        {
            return null;
        }

        var preferenceError = await ApplyPreferencesAsync(
            parseResult,
            projectDirectory,
            workspaceOption,
            suppressNextStepsOption,
            services,
            nextStepRenderer
        );
        if (preferenceError is null)
        {
            return null;
        }

        var exitCode = console.Fail(preferenceError);
        if (streams.JsonCommand is { } jsonCommand)
        {
            CliJsonWriter.WriteFailure(streams.CommandOutput, jsonCommand);
        }

        return exitCode;
    }

    private static async Task<string?> ApplyPreferencesAsync(
        ParseResult parseResult,
        string projectDirectory,
        Option<string?> workspaceOption,
        Option<bool> suppressNextStepsOption,
        CommandServices services,
        NextStepRenderer nextStepRenderer
    )
    {
        nextStepRenderer.Suppress = parseResult.GetValue(suppressNextStepsOption);
        if (
            string.Equals(
                parseResult.CommandResult.Command.Name,
                "config",
                StringComparison.Ordinal
            )
            || string.Equals(
                parseResult.CommandResult.Command.Name,
                "doctor",
                StringComparison.Ordinal
            )
        )
        {
            return null;
        }

        var workspaceDirectory = services.WorkspaceDirectoryResolver.Resolve(
            projectDirectory,
            parseResult.GetValue(workspaceOption)
        );
        var preferences = await services.CliPreferenceService.ResolveAsync(workspaceDirectory);
        if (preferences.Value is not { } effectivePreferences)
        {
            return preferences.Error ?? "Unable to resolve CLI preferences.";
        }

        nextStepRenderer.Suppress |= !effectivePreferences.NextStepsEnabled;
        return null;
    }

    private RootCommand CreateRootCommand(
        string projectDirectory,
        CliConsole console,
        Option<bool> suppressNextStepsOption,
        Option<string?> workspaceOption,
        CommandServices services,
        TextWriter commandOutput,
        TextWriter commandError
    )
    {
        var rootCommand = new RootCommand("Manage LunaPack packs.");
        var logLevelOption = CreateLogLevelOption();
        rootCommand.Options.Add(workspaceOption);
        rootCommand.Options.Add(logLevelOption);
        rootCommand.Options.Add(suppressNextStepsOption);
        var completionProvider = new CliCompletionProvider(
            services.CatalogService,
            services.ProjectStateStore,
            services.WorkspaceDirectoryResolver,
            projectDirectory,
            workspaceOption
        );
        ConfigureRootAction(rootCommand, services, projectDirectory, workspaceOption, console);
        AddPreferenceCommands(rootCommand, services, projectDirectory, workspaceOption, console);
        AddDoctorCommand(rootCommand, projectDirectory, workspaceOption, console);

        AddProjectCommands(
            rootCommand,
            fileSystem,
            services.ProjectStateStore,
            services.UserSettingsStore,
            services.TrustService,
            completionProvider,
            services.WorkspaceDirectoryResolver,
            projectDirectory,
            workspaceOption,
            services.NextStepAdvisor,
            services.NextStepRenderer,
            console,
            services.GitTransportPolicy,
            services.GitRefResolver
        );
        AddCatalogCommands(
            rootCommand,
            services.CatalogService,
            services.CliPreferenceService,
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
        AddAuditAndSbomCommands(
            rootCommand,
            fileSystem,
            services,
            projectDirectory,
            workspaceOption,
            commandOutput,
            commandError,
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

    private async Task<int?> TryRunDoctorAsync(
        string[] args,
        string projectDirectory,
        CliConsole console,
        InvocationConfiguration invocationConfiguration
    )
    {
        var rootCommand = new RootCommand("Manage LunaPack packs.");
        var workspaceOption = CreateWorkspaceOption();
        rootCommand.Options.Add(workspaceOption);
        rootCommand.Options.Add(CreateLogLevelOption());
        rootCommand.Options.Add(CreateSuppressNextStepsOption());
        rootCommand.Add(
            CreateDoctorCommandHandler(console).CreateCommand(projectDirectory, workspaceOption)
        );
        var parseResult = rootCommand.Parse(args);
        return string.Equals(
            parseResult.CommandResult.Command.Name,
            "doctor",
            StringComparison.Ordinal
        )
            ? await parseResult.InvokeAsync(invocationConfiguration)
            : null;
    }

    private void AddDoctorCommand(
        RootCommand rootCommand,
        string projectDirectory,
        Option<string?> workspaceOption,
        CliConsole console
    ) =>
        rootCommand.Add(
            CreateDoctorCommandHandler(console).CreateCommand(projectDirectory, workspaceOption)
        );

    private DoctorCommandHandler CreateDoctorCommandHandler(CliConsole console) =>
        new(CreateDoctorService(console), new WorkspaceDirectoryResolver(fileSystem), console);

    private IDoctorService CreateDoctorService(CliConsole console)
    {
        if (doctorService is not null)
        {
            return doctorService;
        }

        var effectiveGitProcessRunner =
            gitProcessRunner ?? new GitProcessRunner(nonInteractive: true);
        return new DoctorService(
            fileSystem,
            new ProjectStateStore(fileSystem),
            effectiveGitProcessRunner,
            () => userSettingsStore ?? new UserSettingsStore(fileSystem),
            new DoctorCredentialStoreProbe(),
            new DoctorTemporaryFileProbe(fileSystem),
            new DoctorRemoteSourceProbe(
                fileSystem,
                effectiveGitProcessRunner,
                new OciDistributionRegistryClient(
                    TimeSpan.FromSeconds(DoctorService.NetworkSourceTimeoutSeconds),
                    new AzureAcrAccessTokenProvider(console),
                    allowAutoRedirect: false
                ),
                new NativeSourceCredentialStore()
            )
        );
    }

    private static void AddPreferenceCommands(
        RootCommand rootCommand,
        CommandServices services,
        string projectDirectory,
        Option<string?> workspaceOption,
        CliConsole console
    ) =>
        rootCommand.Add(
            new ConfigCommandHandler(
                services.CliPreferenceService,
                services.WorkspaceDirectoryResolver,
                console
            ).CreateCommand(projectDirectory, workspaceOption)
        );

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
                Environment.GetEnvironmentVariable("XDG_DATA_HOME"),
                isWindows,
                OperatingSystem.IsMacOS()
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
        var preferences = CreatePreferenceServices(console);
        var projectStateStore = preferences.ProjectStateStore;
        var effectiveUserSettingsStore = preferences.UserSettingsStore;
        var lifecycleHookHistoryStore = CreateLifecycleHookHistoryStore(effectiveUserSettingsStore);
        var cliPreferenceService = preferences.CliPreferenceService;
        var gitTransportPolicy = preferences.GitTransportPolicy;
        var sourceIgnoreService = preferences.SourceIgnoreService;
        var workspaceDirectoryResolver = new WorkspaceDirectoryResolver(fileSystem);
        var effectiveGitProcessRunner = gitProcessRunner ?? new GitProcessRunner();
        var gitRefResolver = new GitRefResolver(effectiveGitProcessRunner);
        var ociPackSource = CreateOciPackSource(console);
        var packCatalog = CreatePackCatalog(
            console,
            effectiveGitProcessRunner,
            effectiveUserSettingsStore,
            ociPackSource,
            sourceIgnoreService,
            gitTransportPolicy
        );
        var packValidationService = CreatePackValidationService(
            projectStateStore,
            packCatalog,
            console
        );
        var nextStepAdvisor = new NextStepAdvisor(fileSystem, projectStateStore);
        var lifecycleServices = CreateLifecycleServices(
            fileSystem,
            packCatalog,
            projectStateStore,
            effectiveUserSettingsStore,
            console,
            effectiveGitProcessRunner,
            gitRefResolver,
            gitTransportPolicy,
            ociPackSource,
            sourceIgnoreService,
            lifecycleHookHistoryStore
        );
        var packPublishingServices = CreatePackPublishingServices(
            projectStateStore,
            effectiveUserSettingsStore,
            ociPackSource,
            effectiveGitProcessRunner,
            gitRefResolver,
            gitTransportPolicy,
            console,
            sourceIgnoreService
        );
        var linkServices = CreateLinkServices(
            fileSystem,
            projectStateStore,
            console,
            sourceIgnoreService,
            gitTransportPolicy
        );
        return new CommandServices(
            packCatalog,
            ociPackSource,
            projectStateStore,
            effectiveUserSettingsStore,
            lifecycleHookHistoryStore,
            cliPreferenceService,
            workspaceDirectoryResolver,
            new CatalogService(packCatalog, projectStateStore),
            packValidationService,
            packPublishingServices.AuthoringValidationService,
            packPublishingServices.PackPublishingService,
            lifecycleServices,
            linkServices,
            packUpdatePrompter ?? new ConsolePackUpdatePrompter(console),
            nextStepAdvisor,
            nextStepRenderer,
            CreateWorkflowPrerequisiteGuard(nextStepAdvisor, nextStepRenderer, console),
            CreateTrustService(projectStateStore, effectiveUserSettingsStore, console),
            gitTransportPolicy,
            gitRefResolver,
            effectiveGitProcessRunner
        );
    }

    private PreferenceServices CreatePreferenceServices(CliConsole console)
    {
        var effectiveUserSettingsStore = userSettingsStore ?? new UserSettingsStore(fileSystem);
        var projectStateStore = new ProjectStateStore(fileSystem, effectiveUserSettingsStore);
        var cliPreferenceService = new CliPreferenceService(
            fileSystem,
            projectStateStore,
            effectiveUserSettingsStore
        );
        return new PreferenceServices(
            projectStateStore,
            effectiveUserSettingsStore,
            cliPreferenceService,
            new GitTransportPolicy(cliPreferenceService, console),
            new ConfiguredSourceIgnoreService(fileSystem, effectiveUserSettingsStore)
        );
    }

    private LifecycleHookHistoryStore CreateLifecycleHookHistoryStore(
        UserSettingsStore userSettingsStore
    ) => new(fileSystem, userSettingsStore.SettingsDirectory);

    private static WorkflowPrerequisiteGuard CreateWorkflowPrerequisiteGuard(
        NextStepAdvisor nextStepAdvisor,
        NextStepRenderer nextStepRenderer,
        CliConsole console
    ) => new(nextStepAdvisor, nextStepRenderer, console);

    private PackCatalog CreatePackCatalog(
        CliConsole console,
        IGitProcessRunner gitProcessRunner,
        UserSettingsStore settingsStore,
        OciPackSource ociPackSource,
        ConfiguredSourceIgnoreService sourceIgnoreService,
        GitTransportPolicy transportPolicy
    ) =>
        new(
            fileSystem,
            console,
            gitProcessRunner,
            settingsStore,
            ociPackSource,
            sourceIgnoreService,
            transportPolicy: transportPolicy
        );

    private PackValidationService CreatePackValidationService(
        ProjectStateStore projectStateStore,
        PackCatalog packCatalog,
        CliConsole console
    ) =>
        new(
            fileSystem,
            projectStateStore,
            new LocalPackDiscovery(fileSystem, console),
            packCatalog
        );

    private PackAuthoringValidationService CreatePackAuthoringValidationService(
        IGitProcessRunner gitProcessRunner,
        GitRefResolver gitRefResolver,
        GitTransportPolicy transportPolicy,
        CliConsole console
    ) =>
        new(
            fileSystem,
            new ExternalSourceRequirementPlanner(gitRefResolver, transportPolicy),
            new ExternalSourceMaterializer(
                fileSystem,
                gitProcessRunner,
                gitRefResolver,
                transportPolicy
            ),
            new PackInstallationPlanner(fileSystem, new PackTemplateRenderer(fileSystem)),
            console
        );

    private PackPublishingService CreatePackPublishingService(
        ProjectStateStore projectStateStore,
        UserSettingsStore userSettingsStore,
        OciPackSource ociPackSource,
        PackAuthoringValidationService authoringValidationService,
        IGitProcessRunner gitProcessRunner,
        ConfiguredSourceIgnoreService sourceIgnoreService
    ) =>
        new(
            fileSystem,
            projectStateStore,
            userSettingsStore,
            [
                ociPackSource.CreatePublisher(
                    fileSystem,
                    new PackManifestStore(fileSystem),
                    authoringValidationService,
                    gitProcessRunner
                ),
            ],
            sourceIgnoreService
        );

    private (
        PackAuthoringValidationService AuthoringValidationService,
        PackPublishingService PackPublishingService
    ) CreatePackPublishingServices(
        ProjectStateStore projectStateStore,
        UserSettingsStore userSettingsStore,
        OciPackSource ociPackSource,
        IGitProcessRunner gitProcessRunner,
        GitRefResolver gitRefResolver,
        GitTransportPolicy transportPolicy,
        CliConsole console,
        ConfiguredSourceIgnoreService sourceIgnoreService
    )
    {
        var authoringValidationService = CreatePackAuthoringValidationService(
            gitProcessRunner,
            gitRefResolver,
            transportPolicy,
            console
        );
        var packPublishingService = CreatePackPublishingService(
            projectStateStore,
            userSettingsStore,
            ociPackSource,
            authoringValidationService,
            gitProcessRunner,
            sourceIgnoreService
        );
        return (authoringValidationService, packPublishingService);
    }

    private TrustService CreateTrustService(
        ProjectStateStore projectStateStore,
        UserSettingsStore userSettingsStore,
        CliConsole console
    ) =>
        new(
            fileSystem,
            projectStateStore,
            userSettingsStore,
            trustConfirmer ?? new ConsoleTrustConfirmer(console)
        );

    private OciPackSource CreateOciPackSource(CliConsole console) =>
        new(fileSystem, console, accessTokenProvider: new AzureAcrAccessTokenProvider(console));

    private static void AddCatalogCommands(
        RootCommand rootCommand,
        CatalogService catalogService,
        CliPreferenceService cliPreferenceService,
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
                cliPreferenceService,
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
                cliPreferenceService,
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
        CliPreferenceService cliPreferenceService,
        WorkspaceDirectoryResolver workspaceDirectoryResolver,
        NextStepAdvisor nextStepAdvisor,
        NextStepRenderer nextStepRenderer,
        GitRefResolver gitRefResolver,
        GitTransportPolicy transportPolicy,
        PackAuthoringValidationService authoringValidationService,
        string projectDirectory,
        Option<string?> workspaceOption,
        CliConsole console
    ) =>
        rootCommand.Add(
            new PackAuthoringCommandHandler(
                fileSystem,
                new PackManifestStore(fileSystem),
                cliPreferenceService,
                workspaceDirectoryResolver,
                nextStepAdvisor,
                nextStepRenderer,
                console,
                gitRefResolver,
                transportPolicy,
                authoringValidationService
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
        rootCommand.Add(
            new LifecycleHookHistoryCommandHandler(
                services.LifecycleHookHistoryStore,
                console
            ).CreateCommand()
        );
        AddPackAuthoringCommand(
            rootCommand,
            fileSystem,
            services.CliPreferenceService,
            services.WorkspaceDirectoryResolver,
            services.NextStepAdvisor,
            services.NextStepRenderer,
            services.GitRefResolver,
            services.GitTransportPolicy,
            services.PackAuthoringValidationService,
            projectDirectory,
            workspaceOption,
            console
        );
        rootCommand.Add(
            new PublishPackCommandHandler(
                fileSystem,
                services.PackPublishingService,
                completionProvider,
                services.WorkspaceDirectoryResolver,
                console
            ).CreateCommand(projectDirectory, workspaceOption)
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
        GitRefResolver gitRefResolver,
        GitTransportPolicy transportPolicy,
        OciPackSource ociPackSource,
        ConfiguredSourceIgnoreService sourceIgnoreService,
        LifecycleHookHistoryStore lifecycleHookHistoryStore
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
            transportPolicy,
            new LifecycleHookPolicyStore(fileSystem, userSettingsStore.SettingsDirectory),
            new GitPackMaterializer(
                fileSystem,
                gitProcessRunner,
                console,
                transportPolicy,
                ociPackSource: ociPackSource
            ),
            configuredHookAuthorizer: new LifecycleHookAuthorizer(
                userSettingsStore,
                new TrustPolicy(fileSystem),
                new LifecycleCommandResolver(fileSystem),
                new ConsoleLifecycleHookConfirmer(console)
            ),
            configuredHookExecutor: new LifecycleHookExecutor(
                fileSystem,
                console,
                lifecycleHookHistoryStore
            ),
            configuredExternalSourceRequirementPlanner: new ExternalSourceRequirementPlanner(
                gitRefResolver,
                transportPolicy
            ),
            configuredExternalSourceMaterializer: new ExternalSourceMaterializer(
                fileSystem,
                gitProcessRunner,
                gitRefResolver,
                transportPolicy
            ),
            configuredExternalSourceConsentCoordinator: new ExternalSourceConsentCoordinator(
                console.IsInteractive
                    ? new ConsoleExternalSourceApprover(console)
                    : new DenyExternalSourceApprover(),
                console.IsInteractive
                    ? new ConsoleExternalSourceIdentifierPrompter(console)
                    : new DenyExternalSourceIdentifierPrompter()
            ),
            configuredSourceIgnoreService: sourceIgnoreService
        );
        var updateSelectionService = new PackUpdateSelectionService(
            packCatalog,
            projectStateStore,
            packLifecycleService,
            sourceIgnoreService
        );
        return new LifecycleServices(
            packLifecycleService,
            new PackUpdateService(
                fileSystem,
                packCatalog,
                packLifecycleService,
                projectStateStore,
                new ConsoleSourceSwitchConfirmer(console),
                sourceIgnoreService
            ),
            updateSelectionService
        );
    }

    private static void AddProjectCommands(
        RootCommand rootCommand,
        IFileSystem fileSystem,
        ProjectStateStore projectStateStore,
        UserSettingsStore userSettingsStore,
        TrustService trustService,
        CliCompletionProvider completionProvider,
        WorkspaceDirectoryResolver workspaceDirectoryResolver,
        string projectDirectory,
        Option<string?> workspaceOption,
        NextStepAdvisor nextStepAdvisor,
        NextStepRenderer nextStepRenderer,
        CliConsole console,
        GitTransportPolicy transportPolicy,
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
                transportPolicy,
                gitRefResolver,
                completionProvider,
                userSettingsStore
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
                fileSystem,
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

    private static void AddAuditAndSbomCommands(
        RootCommand rootCommand,
        IFileSystem fileSystem,
        CommandServices services,
        string projectDirectory,
        Option<string?> workspaceOption,
        TextWriter commandOutput,
        TextWriter commandError,
        CliConsole console
    )
    {
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
        AddSbomCommand(
            rootCommand,
            fileSystem,
            services.ProjectStateStore,
            services.WorkspaceDirectoryResolver,
            projectDirectory,
            workspaceOption,
            commandOutput,
            commandError,
            TimeProvider.System
        );
    }

    private static void AddSbomCommand(
        RootCommand rootCommand,
        IFileSystem fileSystem,
        ProjectStateStore projectStateStore,
        WorkspaceDirectoryResolver workspaceDirectoryResolver,
        string projectDirectory,
        Option<string?> workspaceOption,
        TextWriter commandOutput,
        TextWriter commandError,
        TimeProvider timeProvider
    ) =>
        rootCommand.Add(
            new SbomCommandHandler(
                fileSystem,
                projectStateStore,
                workspaceDirectoryResolver,
                commandOutput,
                commandError,
                timeProvider
            ).CreateCommand(projectDirectory, workspaceOption)
        );

    private static LinkServices CreateLinkServices(
        IFileSystem fileSystem,
        ProjectStateStore projectStateStore,
        CliConsole console,
        ConfiguredSourceIgnoreService sourceIgnoreService,
        GitTransportPolicy transportPolicy
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
                    new GitLinkCache(fileSystem, LinkSourceCacheRoot.Resolve(fileSystem)),
                    transportPolicy
                ),
            ],
            sourceIgnoreService
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

    private sealed record PreferenceServices(
        ProjectStateStore ProjectStateStore,
        UserSettingsStore UserSettingsStore,
        CliPreferenceService CliPreferenceService,
        GitTransportPolicy GitTransportPolicy,
        ConfiguredSourceIgnoreService SourceIgnoreService
    );

    private sealed record LifecycleServices(
        PackLifecycleService PackLifecycleService,
        PackUpdateService PackUpdateService,
        PackUpdateSelectionService PackUpdateSelectionService
    );

    private sealed record CommandServices(
        PackCatalog PackCatalog,
        OciPackSource OciPackSource,
        ProjectStateStore ProjectStateStore,
        UserSettingsStore UserSettingsStore,
        LifecycleHookHistoryStore LifecycleHookHistoryStore,
        CliPreferenceService CliPreferenceService,
        WorkspaceDirectoryResolver WorkspaceDirectoryResolver,
        CatalogService CatalogService,
        PackValidationService PackValidationService,
        PackAuthoringValidationService PackAuthoringValidationService,
        PackPublishingService PackPublishingService,
        LifecycleServices LifecycleServices,
        LinkServices LinkServices,
        IPackUpdatePrompter PackUpdatePrompter,
        NextStepAdvisor NextStepAdvisor,
        NextStepRenderer NextStepRenderer,
        WorkflowPrerequisiteGuard PrerequisiteGuard,
        TrustService TrustService,
        GitTransportPolicy GitTransportPolicy,
        GitRefResolver GitRefResolver,
        IGitProcessRunner GitProcessRunner
    ) : IDisposable
    {
        public void Dispose()
        {
            PackCatalog.Dispose();
            OciPackSource.Dispose();
        }
    }

    private sealed record InvocationBootstrap(CliConsole? Console, int ExitCode);

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
