using System.IO.Abstractions;
using Lunapack.Cli.Application.CommandExecution;
using Lunapack.Cli.Project;

namespace Lunapack.Cli.Application.Guidance;

internal sealed class NextStepAdvisor(IFileSystem fileSystem, IProjectStateStore projectStateStore)
{
    private readonly int _maximumRecommendations = 3;

    public async Task<ManifestOperationResult<WorkspaceGuidance>> InspectWorkspaceAsync(
        string projectDirectory
    )
    {
        var configurationExists = fileSystem.File.Exists(
            fileSystem.Path.Combine(projectDirectory, ProjectStateStore.ConfigurationFileName)
        );
        var lockFileExists = fileSystem.File.Exists(
            fileSystem.Path.Combine(projectDirectory, ProjectStateStore.LockFileName)
        );
        if (!configurationExists && !lockFileExists)
        {
            return ManifestOperationResult<WorkspaceGuidance>.Success(
                CreateGuidance(WorkspaceStage.NoWorkspace, 0, 0)
            );
        }

        var loadedState = await projectStateStore.LoadAsync(projectDirectory);
        if (loadedState.Value is not { } state)
        {
            return ManifestOperationResult<WorkspaceGuidance>.Failure(
                loadedState.Error ?? "Unable to inspect workspace state."
            );
        }

        var sourceCount = state.Configuration.Sources.Count;
        var installedPackCount = state.Configuration.Packs.Count;
        var stage =
            installedPackCount > 0 ? WorkspaceStage.ActiveWorkspace
            : sourceCount > 0 ? WorkspaceStage.SourcesConfigured
            : WorkspaceStage.EmptyWorkspace;
        return ManifestOperationResult<WorkspaceGuidance>.Success(
            CreateGuidance(stage, sourceCount, installedPackCount)
        );
    }

    public IReadOnlyList<NextStepRecommendation> Recommend(
        NextStepContext context,
        string? value = null
    ) => CreateRecommendations(context, value).Take(_maximumRecommendations).ToList();

    private WorkspaceGuidance CreateGuidance(
        WorkspaceStage stage,
        int sourceCount,
        int installedPackCount
    )
    {
        var context = stage switch
        {
            WorkspaceStage.NoWorkspace => NextStepContext.MissingWorkspace,
            WorkspaceStage.EmptyWorkspace => NextStepContext.MissingSources,
            WorkspaceStage.SourcesConfigured => NextStepContext.SourceAdded,
            WorkspaceStage.ActiveWorkspace => NextStepContext.PackInstalled,
            _ => throw new InvalidOperationException($"Unsupported workspace stage '{stage}'."),
        };
        return new WorkspaceGuidance(stage, sourceCount, installedPackCount, Recommend(context));
    }

    private static IReadOnlyList<NextStepRecommendation> CreateRecommendations(
        NextStepContext context,
        string? value
    ) =>
        context switch
        {
            NextStepContext.PackManifestMissing
            or NextStepContext.PackManifestPresent
            or NextStepContext.PackInitialized
            or NextStepContext.PackSourceAdded
            or NextStepContext.UnknownPackSourceAlias
            or NextStepContext.PackModified
            or NextStepContext.PackDisplayed
            or NextStepContext.PackValidated => CreatePackAuthoringRecommendations(context, value),
            NextStepContext.WorkspaceInitialized
            or NextStepContext.SourceAdded
            or NextStepContext.SourcesRemain
            or NextStepContext.NoSourcesRemain
            or NextStepContext.MissingWorkspace
            or NextStepContext.MissingSources => CreateWorkspaceRecommendations(context),
            NextStepContext.PacksDiscovered
            or NextStepContext.PacksSearched
            or NextStepContext.PackInspected
            or NextStepContext.PackNotFound => CreateCatalogRecommendations(context, value),
            NextStepContext.SourceApprovalRejected
            or NextStepContext.PackInstalled
            or NextStepContext.PacksUpdated
            or NextStepContext.PacksRemain
            or NextStepContext.NoPacksRemain => CreatePackLifecycleRecommendations(context, value),
            NextStepContext.LinkAdded or NextStepContext.LinkInstalled => CreateLinkRecommendations(
                context,
                value
            ),
            _ => throw new InvalidOperationException($"Unsupported next-step context '{context}'."),
        };

    private static IReadOnlyList<NextStepRecommendation> CreatePackAuthoringRecommendations(
        NextStepContext context,
        string? value
    ) =>
        context switch
        {
            NextStepContext.PackManifestMissing =>
            [
                new("Create a pack manifest", "luna pack init"),
            ],
            NextStepContext.PackManifestPresent =>
            [
                new("Show the manifest", "luna pack show"),
                new("Add a managed file", "luna pack add file <path>"),
                new("Validate the manifest", "luna pack validate"),
            ],
            NextStepContext.PackInitialized =>
            [
                new("Add a managed file", "luna pack add file <path>"),
                new("Add a lifecycle hook", "luna pack add hook instruction <event> <file>"),
                new(
                    "Add an external GitHub source",
                    "luna pack add source github <name> <owner/repository> --ref <ref>"
                ),
            ],
            NextStepContext.PackSourceAdded =>
            [
                new(
                    "Add source-backed content",
                    $"luna pack add file <path> --source {value ?? "<name>"}"
                ),
                new("Validate the manifest", "luna pack validate"),
            ],
            NextStepContext.UnknownPackSourceAlias =>
            [
                new(
                    "Add a GitHub source",
                    $"luna pack add source github {value ?? "<name>"} <owner/repository> --ref <ref>"
                ),
                new(
                    "Add a Git source",
                    $"luna pack add source git {value ?? "<name>"} <repository-url> --ref <ref>"
                ),
            ],
            NextStepContext.PackModified =>
            [
                new("Show the manifest", "luna pack show"),
                new("Validate the manifest", "luna pack validate"),
            ],
            NextStepContext.PackDisplayed =>
            [
                new("Add a managed file", "luna pack add file <path>"),
                new("Add a lifecycle hook", "luna pack add hook instruction <event> <file>"),
                new("Validate the manifest", "luna pack validate"),
            ],
            NextStepContext.PackValidated =>
            [
                new("Show the manifest", "luna pack show"),
                new("Add a managed file", "luna pack add file <path>"),
                new("List lifecycle hooks", "luna pack hooks"),
            ],
            _ => throw new InvalidOperationException($"Unsupported next-step context '{context}'."),
        };

    private static IReadOnlyList<NextStepRecommendation> CreateWorkspaceRecommendations(
        NextStepContext context
    ) =>
        context switch
        {
            NextStepContext.WorkspaceInitialized =>
            [
                new("Add a source", "luna sources add git <name> <repository-url>"),
                new("View sources", "luna sources list"),
            ],
            NextStepContext.SourceAdded =>
            [
                new("Discover available packs", "luna discover"),
                new("Search packs", "luna search <keyword>"),
                new("Install a pack", "luna install <pack>"),
            ],
            NextStepContext.SourcesRemain =>
            [
                new("View sources", "luna sources list"),
                new("Discover available packs", "luna discover"),
            ],
            NextStepContext.NoSourcesRemain or NextStepContext.MissingSources =>
            [
                new("Add a source", "luna sources add git <name> <repository-url>"),
            ],
            NextStepContext.MissingWorkspace => [new("Initialize a workspace", "luna init")],
            _ => throw new InvalidOperationException($"Unsupported next-step context '{context}'."),
        };

    private static IReadOnlyList<NextStepRecommendation> CreateCatalogRecommendations(
        NextStepContext context,
        string? value
    ) =>
        context switch
        {
            NextStepContext.PacksDiscovered => [new("Install a pack", "luna install <pack>")],
            NextStepContext.PacksSearched =>
            [
                new("Inspect a pack", "luna inspect <pack>"),
                new("Install a pack", "luna install <pack>"),
            ],
            NextStepContext.PackInspected =>
            [
                new("Install the pack", $"luna install {value ?? "<pack>"}"),
                new("Discover available packs", "luna discover"),
            ],
            NextStepContext.PackNotFound =>
            [
                new("Search for the pack", $"luna search {value ?? "<keyword>"}"),
                new("Discover available packs", "luna discover"),
            ],
            _ => throw new InvalidOperationException($"Unsupported next-step context '{context}'."),
        };

    private static IReadOnlyList<NextStepRecommendation> CreatePackLifecycleRecommendations(
        NextStepContext context,
        string? value
    ) =>
        context switch
        {
            NextStepContext.SourceApprovalRejected =>
            [
                new("Inspect the pack", $"luna inspect {value ?? "<pack>"}"),
                new(
                    "Configure the source manually",
                    "luna sources add git <name> <repository-url> --ref <ref>"
                ),
            ],
            NextStepContext.PackInstalled =>
            [
                new("Check for updates", "luna outdated"),
                new("Update installed packs", "luna update"),
                new("Remove the pack", value is null ? "luna audit" : $"luna uninstall {value}"),
            ],
            NextStepContext.PacksUpdated =>
            [
                new("Audit installed packs", "luna audit"),
                new("Check for updates", "luna outdated"),
            ],
            NextStepContext.PacksRemain =>
            [
                new("Discover available packs", "luna discover"),
                new("Install a pack", "luna install <pack>"),
            ],
            NextStepContext.NoPacksRemain =>
            [
                new("Discover available packs", "luna discover"),
                new("Search packs", "luna search <keyword>"),
            ],
            _ => throw new InvalidOperationException($"Unsupported next-step context '{context}'."),
        };

    private static IReadOnlyList<NextStepRecommendation> CreateLinkRecommendations(
        NextStepContext context,
        string? value
    ) =>
        context switch
        {
            NextStepContext.LinkAdded =>
            [
                new("Install the link", $"luna install {value ?? "<link>"}"),
                new("Show the link", $"luna links show {value ?? "<link>"}"),
            ],
            NextStepContext.LinkInstalled =>
            [
                new("Audit managed files", "luna audit"),
                new("Check for updates", "luna outdated"),
                new(
                    "Remove the link",
                    value is null ? "luna links list" : $"luna uninstall {value}"
                ),
            ],
            _ => throw new InvalidOperationException($"Unsupported next-step context '{context}'."),
        };
}
