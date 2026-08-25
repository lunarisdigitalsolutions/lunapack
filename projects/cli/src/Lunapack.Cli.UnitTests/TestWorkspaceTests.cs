namespace Lunapack.Cli.UnitTests;

public sealed class TestWorkspaceTests
{
    [Test]
    public async Task Workspace_WhenCreated_UsesAnExistingDirectory()
    {
        using var workspace = new TestWorkspace();

        await Assert.That(Directory.Exists(workspace.Path)).IsTrue();
    }
}
