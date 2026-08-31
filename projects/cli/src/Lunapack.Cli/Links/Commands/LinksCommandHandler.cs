using System.CommandLine;
using Lunapack.Cli.Application;
using Lunapack.Cli.Application.Guidance;
using Lunapack.Cli.Project;

namespace Lunapack.Cli.Links.Commands;

internal sealed class LinksCommandHandler(
    IProjectStateStore projectStateStore,
    LinkDefinitionFactory linkDefinitionFactory,
    LinkLifecycleService linkLifecycleService,
    LinkInspectionService linkInspectionService,
    WorkspaceDirectoryResolver workspaceDirectoryResolver,
    NextStepAdvisor nextStepAdvisor,
    NextStepRenderer nextStepRenderer,
    CliConsole console
)
{
    public Command CreateCommand(string projectDirectory, Option<string?> workspaceOption)
    {
        return new Command("links", "Manage project-owned file links.")
        {
            CreateAddCommand(projectDirectory, workspaceOption),
            CreateListCommand(projectDirectory, workspaceOption),
            CreateShowCommand(projectDirectory, workspaceOption),
            CreateRemoveCommand(projectDirectory, workspaceOption),
        };
    }

    private Command CreateAddCommand(string projectDirectory, Option<string?> workspaceOption)
    {
        var nameArgument = CreateLinkNameArgument();
        var sourceOption = CreateSourceOption();
        var includeOption = CreateIncludeOption();
        var excludeOption = CreateExcludeOption();
        var pathOption = CreatePathOption();
        var targetOption = CreateTargetOption();
        var refOption = CreateRefOption();
        var stripPrefixOption = CreateStripPrefixOption();
        var flattenOption = CreateFlattenOption();
        var installOption = CreateInstallOption();
        var forceOption = CreateForceOption();
        var command = new Command("add", "Add a project-owned link.")
        {
            nameArgument,
            sourceOption,
            includeOption,
            excludeOption,
            pathOption,
            targetOption,
            refOption,
            stripPrefixOption,
            flattenOption,
            installOption,
            forceOption,
        };
        command.SetAction(async parseResult =>
            await AddAsync(
                workspaceDirectoryResolver.Resolve(
                    projectDirectory,
                    parseResult.GetValue(workspaceOption)
                ),
                parseResult.GetValue(nameArgument),
                new LinkDefinitionRequest(
                    parseResult.GetValue(sourceOption),
                    parseResult.GetValue(includeOption) ?? [],
                    parseResult.GetValue(excludeOption) ?? [],
                    parseResult.GetValue(pathOption),
                    parseResult.GetValue(targetOption),
                    parseResult.GetValue(refOption),
                    parseResult.GetValue(stripPrefixOption),
                    parseResult.GetValue(flattenOption)
                ),
                parseResult.GetValue(installOption),
                parseResult.GetValue(forceOption)
            )
        );

        return command;
    }

    private static Argument<string> CreateLinkNameArgument() =>
        new("name") { Description = "Unique link name using pack-ID syntax." };

    private static Option<string?> CreateSourceOption() =>
        new("--source", "-s") { Description = "Configured source name to select files from." };

    private static Option<string[]> CreateIncludeOption() =>
        new("--include", "-i") { Description = "File, directory, or glob selector to include." };

    private static Option<string[]> CreateExcludeOption() =>
        new("--exclude", "-e") { Description = "Glob pattern to exclude from the selection." };

    private static Option<string?> CreatePathOption() =>
        new("--path") { Description = "Source-relative base path for selectors." };

    private static Option<string?> CreateTargetOption() =>
        new("--target", "-t") { Description = "Workspace-relative directory for selected files." };

    private static Option<string?> CreateRefOption() =>
        new("--ref") { Description = "Git ref that overrides the configured source ref." };

    private static Option<string?> CreateStripPrefixOption() =>
        new("--strip-prefix") { Description = "Path prefix removed from every selected file." };

    private static Option<bool> CreateFlattenOption() =>
        new("--flatten") { Description = "Map every selected file directly beneath the target." };

    private static Option<bool> CreateInstallOption() =>
        new("--install") { Description = "Install the link after persisting its definition." };

    private static Option<bool> CreateForceOption() =>
        new("--force") { Description = "Replace an existing link definition." };

    private Command CreateListCommand(string projectDirectory, Option<string?> workspaceOption)
    {
        var command = new Command("list", "List configured links.");
        command.SetAction(async parseResult =>
            await ListAsync(
                workspaceDirectoryResolver.Resolve(
                    projectDirectory,
                    parseResult.GetValue(workspaceOption)
                )
            )
        );

        return command;
    }

    private Command CreateShowCommand(string projectDirectory, Option<string?> workspaceOption)
    {
        var nameArgument = new Argument<string>("name")
        {
            Description = "Name of the configured link to show.",
        };
        var command = new Command("show", "Show a configured link.") { nameArgument };
        command.SetAction(async parseResult =>
            await ShowAsync(
                workspaceDirectoryResolver.Resolve(
                    projectDirectory,
                    parseResult.GetValue(workspaceOption)
                ),
                parseResult.GetValue(nameArgument)
            )
        );

        return command;
    }

    private Command CreateRemoveCommand(string projectDirectory, Option<string?> workspaceOption)
    {
        var nameArgument = new Argument<string>("name")
        {
            Description = "Name of the configured link to remove.",
        };
        var forceOption = new Option<bool>("--force")
        {
            Description = "Remove an installed link definition and its ownership records.",
        };
        var command = new Command("rm", "Remove a configured link.") { nameArgument, forceOption };
        command.SetAction(async parseResult =>
        {
            var name = parseResult.GetValue(nameArgument);
            return name is null
                ? console.Fail("A link name is required.")
                : await linkLifecycleService.RemoveAsync(
                    workspaceDirectoryResolver.Resolve(
                        projectDirectory,
                        parseResult.GetValue(workspaceOption)
                    ),
                    name,
                    parseResult.GetValue(forceOption)
                );
        });

        return command;
    }

    public async Task<int> AddAsync(
        string projectDirectory,
        string? name,
        LinkDefinitionRequest request,
        bool install,
        bool force
    )
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrEmpty(name))
        {
            return console.Fail("A link name is required.");
        }

        var loadedState = await projectStateStore.LoadAsync(projectDirectory);
        if (loadedState.Value is not { } state)
        {
            return console.Fail(loadedState.Error);
        }

        var definition = linkDefinitionFactory.Create(
            projectDirectory,
            state,
            name,
            request,
            force
        );
        if (definition.Value is not { } link)
        {
            return console.Fail(definition.Error);
        }

        state.Configuration.Links[name] = link;
        if (!install)
        {
            var savedState = await projectStateStore.SaveAsync(projectDirectory, state);
            if (!savedState.IsSuccess)
            {
                return console.Fail(savedState.Error);
            }

            console.Success($"Link '{name}' added");
            nextStepRenderer.Render(nextStepAdvisor.Recommend(NextStepContext.LinkAdded, name));
            return 0;
        }

        TimeSpan? managedFileChangesDuration = null;
        var installed = await linkLifecycleService.InstallAsync(
            projectDirectory,
            name,
            allowReinstall: true,
            preparedState: state,
            onManagedFileChangesApplied: duration => managedFileChangesDuration = duration
        );
        if (installed != 0)
        {
            return installed;
        }

        console.Success(
            $"Link '{name}' installed in {CliDuration.Format(managedFileChangesDuration ?? TimeSpan.Zero)}"
        );
        nextStepRenderer.Render(nextStepAdvisor.Recommend(NextStepContext.LinkInstalled, name));
        return 0;
    }

    public async Task<int> ListAsync(string projectDirectory)
    {
        var summaries = await linkInspectionService.ListAsync(projectDirectory);
        if (summaries.Value is not { } links)
        {
            return console.Fail(summaries.Error);
        }

        if (links.Count == 0)
        {
            console.Info("No links are configured.");
            return 0;
        }

        console.Render(LinkOutputFormatter.CreateListTable(links));
        return 0;
    }

    public async Task<int> ShowAsync(string projectDirectory, string? name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return console.Fail("A link name is required.");
        }

        var details = await linkInspectionService.ShowAsync(projectDirectory, name);
        if (details.Value is not { } link)
        {
            return console.Fail(details.Error);
        }

        console.Render(LinkOutputFormatter.CreateDetailTable(link));
        return 0;
    }
}
