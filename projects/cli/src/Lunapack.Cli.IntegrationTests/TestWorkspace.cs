namespace Lunapack.Cli.IntegrationTests;

internal sealed class TestWorkspace : IDisposable
{
    public TestWorkspace()
    {
        Path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "lunapack-tests",
            Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public void Dispose()
    {
        if (Directory.Exists(Path))
        {
            foreach (
                var filePath in Directory.EnumerateFiles(Path, "*", SearchOption.AllDirectories)
            )
            {
                File.SetAttributes(filePath, FileAttributes.Normal);
            }

            Directory.Delete(Path, recursive: true);
        }
    }
}
