namespace Lunapack.Cli.UnitTests.Project;

public sealed class ProjectInitializationCommandHandlerTests
{
    [Test]
    public async Task Initialize_WhenProjectStateMissing_CreatesSchemaValidDocuments()
    {
        using var workspace = new TestWorkspace();
        var console = TestConsole.Create();
        var handler = new ProjectInitializationCommandHandler(
            workspace.FileSystem,
            workspace.StateStore,
            new WorkspaceDirectoryResolver(workspace.FileSystem),
            new NextStepAdvisor(workspace.FileSystem, workspace.StateStore),
            new NextStepRenderer(console),
            console
        );

        var exitCode = await handler.InitializeAsync(workspace.Path);
        var state = await workspace.StateStore.LoadAsync(workspace.Path);

        await Assert.That(exitCode).IsEqualTo(0);
        await Assert.That(state.IsSuccess).IsTrue();
        var projectState = state.RequireValue();
        await Assert.That(projectState.Configuration.SchemaVersion).IsEqualTo(1);
        await Assert.That(projectState.Configuration.Variables).IsEmpty();
        await Assert.That(projectState.LockFile.SchemaVersion).IsEqualTo(1);
        var configuration = workspace.FileSystem.File.ReadAllText(
            workspace.FileSystem.Path.Combine(
                workspace.Path,
                ProjectStateStore.ConfigurationFileName
            )
        );
        await Assert.That(configuration).Contains("variables: {}");
    }
}
