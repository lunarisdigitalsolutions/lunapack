namespace Lunapack.Cli.UnitTests;

public sealed class NextStepAdvisorTests
{
    [Test]
    public async Task InspectWorkspace_WhenStateAdvances_ClassifiesEveryMaturityStage()
    {
        using var workspace = new TestWorkspace();
        var advisor = new NextStepAdvisor(workspace.FileSystem, workspace.StateStore);

        var missing = await advisor.InspectWorkspaceAsync(workspace.Path);
        await workspace.Application.RunAsync(["init"], workspace.Path);
        var empty = await advisor.InspectWorkspaceAsync(workspace.Path);
        CreatePack(workspace.Path);
        await workspace.Application.RunAsync(
            ["sources", "add", "local", "local", "source"],
            workspace.Path
        );
        var sourced = await advisor.InspectWorkspaceAsync(workspace.Path);
        await workspace.Application.RunAsync(["install", "example"], workspace.Path);
        var active = await advisor.InspectWorkspaceAsync(workspace.Path);

        await Assert.That(missing.RequireValue().Stage).IsEqualTo(WorkspaceStage.NoWorkspace);
        await Assert.That(empty.RequireValue().Stage).IsEqualTo(WorkspaceStage.EmptyWorkspace);
        await Assert.That(sourced.RequireValue().Stage).IsEqualTo(WorkspaceStage.SourcesConfigured);
        await Assert.That(active.RequireValue().Stage).IsEqualTo(WorkspaceStage.ActiveWorkspace);
    }

    [Test]
    public async Task InspectWorkspace_WhenOnlyOneStateFileExists_ReturnsFailure()
    {
        using var workspace = new TestWorkspace();
        workspace.FileSystem.File.WriteAllText(
            workspace.FileSystem.Path.Combine(
                workspace.Path,
                ProjectStateStore.ConfigurationFileName
            ),
            "schemaVersion: 1\nsources: []\npacks: []\ntrust:\n  sources: []\n  packs: []\nvariables: {}\n"
        );
        var advisor = new NextStepAdvisor(workspace.FileSystem, workspace.StateStore);

        var result = await advisor.InspectWorkspaceAsync(workspace.Path);

        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That(result.Error).Contains(ProjectStateStore.LockFileName);
    }

    [Test]
    public async Task Recommend_WhenContextHasSeveralActions_PreservesOrderAndLimit()
    {
        using var workspace = new TestWorkspace();
        var advisor = new NextStepAdvisor(workspace.FileSystem, workspace.StateStore);

        var recommendations = advisor.Recommend(NextStepContext.SourceAdded);

        await Assert.That(recommendations).Count().IsEqualTo(3);
        await Assert.That(recommendations[0].Command).IsEqualTo("luna discover");
        await Assert.That(recommendations[2].Command).IsEqualTo("luna install <pack>");
    }

    [Test]
    public async Task Recommend_WhenAuthoringPack_UsesHookAndExternalSourceActions()
    {
        using var workspace = new TestWorkspace();
        var advisor = new NextStepAdvisor(workspace.FileSystem, workspace.StateStore);

        var initialized = advisor.Recommend(NextStepContext.PackInitialized);
        var validated = advisor.Recommend(NextStepContext.PackValidated);
        var added = advisor.Recommend(NextStepContext.PackSourceAdded, "upstream");
        var unknown = advisor.Recommend(NextStepContext.UnknownPackSourceAlias, "upstream");
        var rejected = advisor.Recommend(NextStepContext.SourceApprovalRejected, "example");

        await Assert
            .That(new[] { initialized, added, unknown, rejected }.All(items => items.Count <= 3))
            .IsTrue();
        await Assert
            .That(initialized.Select(item => item.Command))
            .Contains("luna pack add source github <name> <owner/repository> --ref <ref>");
        await Assert
            .That(initialized.Select(item => item.Command))
            .Contains("luna pack add hook instruction <event> <file>");
        await Assert
            .That(added.Select(item => item.Command))
            .Contains("luna pack add file <path> --source upstream");
        await Assert
            .That(unknown.Select(item => item.Command))
            .Contains("luna pack add source git upstream <repository-url> --ref <ref>");
        await Assert.That(rejected.Select(item => item.Command)).Contains("luna inspect example");
        await Assert.That(validated.Select(item => item.Command)).Contains("luna pack hooks");
    }

    [Test]
    public async Task Recommend_WhenUpdateCompletes_DoesNotSuggestSourceCleanup()
    {
        using var workspace = new TestWorkspace();
        var advisor = new NextStepAdvisor(workspace.FileSystem, workspace.StateStore);

        var recommendations = advisor.Recommend(NextStepContext.PacksUpdated);

        await Assert
            .That(
                recommendations.Any(item =>
                    item.Command.Contains("sources rm", StringComparison.Ordinal)
                )
            )
            .IsFalse();
    }

    private static void CreatePack(string workspacePath)
    {
        var packDirectory = Path.Combine(workspacePath, "source", "example");
        var templatesDirectory = Path.Combine(packDirectory, "templates");
        Directory.CreateDirectory(templatesDirectory);
        File.WriteAllText(
            Path.Combine(packDirectory, "pack.yml"),
            "id: example\nversion: 1.0.0\nlicense: MIT\nauthor: Lunaris Digital Solutions <info@lunaris.digital>\nmanagedFiles:\n  - source: templates/example.txt\n    target: example.txt\n"
        );
        File.WriteAllText(Path.Combine(templatesDirectory, "example.txt"), "example");
    }
}
