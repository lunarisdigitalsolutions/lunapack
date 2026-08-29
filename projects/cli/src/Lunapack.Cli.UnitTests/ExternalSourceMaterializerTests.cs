using Lunapack.Cli.Application.CommandExecution;
using Lunapack.Cli.Packs.ExternalSources;
using Lunapack.Cli.Project;
using Lunapack.Cli.Sources;
using Lunapack.Cli.Sources.Git;

namespace Lunapack.Cli.UnitTests;

public sealed class ExternalSourceMaterializerTests
{
    [Test]
    public async Task MaterializeAsync_WhenSourceResolves_CreatesRootsForEveryPackAliasOnce()
    {
        var runner = new MaterializingGitProcessRunner(createBasePath: true);
        using var workspace = new TestWorkspace(gitProcessRunner: runner);
        var plan = CreatePlan([
            new ExternalSourceRequirementUse("first", "1.0.0", "upstream", null, 1),
            new ExternalSourceRequirementUse("second", "1.0.0", "shared", null, 1),
        ]);
        var materializer = new ExternalSourceMaterializer(
            workspace.FileSystem,
            runner,
            new GitRefResolver(runner)
        );

        await using var materialization = (await materializer.MaterializeAsync(plan)).Value;

        await Assert.That(materialization).IsNotNull();
        await Assert.That(materialization?.Roots.Find("first", "upstream")).IsNotNull();
        await Assert.That(materialization?.Roots.Find("second", "shared")).IsNotNull();
        await Assert.That(runner.CheckoutCount).IsEqualTo(1);
    }

    [Test]
    public async Task MaterializeAsync_WhenBasePathMissing_ReturnsFailure()
    {
        var runner = new MaterializingGitProcessRunner(createBasePath: false);
        using var workspace = new TestWorkspace(gitProcessRunner: runner);
        var materializer = new ExternalSourceMaterializer(
            workspace.FileSystem,
            runner,
            new GitRefResolver(runner)
        );

        var result = await materializer.MaterializeAsync(
            CreatePlan([new ExternalSourceRequirementUse("pack", "1.0.0", "upstream", null, 1)])
        );

        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That(result.Error).Contains("base path");
    }

    private static ExternalSourceRequirementPlan CreatePlan(
        IReadOnlyList<ExternalSourceRequirementUse> uses
    )
    {
        var source = new ProjectConfiguration.GitSource
        {
            Name = "upstream",
            Url = "https://github.com/example/standards.git",
            Ref = "refs/heads/main",
            Path = "docs",
        };
        var fingerprint = SourceIdentityNormalizer.Create(source).RequireValue();
        var group = new ExternalSourceRequirementGroup(
            fingerprint,
            source,
            uses,
            source.Name,
            false,
            null
        );
        return new ExternalSourceRequirementPlan(
            [group],
            [
                .. uses.Select(use => new ExternalSourceAliasMapping(
                    use.PackId,
                    use.PackVersion,
                    use.Alias,
                    source.Name,
                    fingerprint
                )),
            ]
        );
    }

    private sealed class MaterializingGitProcessRunner(bool createBasePath) : IGitProcessRunner
    {
        public int CheckoutCount { get; private set; }

        public Task<ManifestOperationResult<GitProcessOutput>> RunAsync(
            IReadOnlyList<string> arguments,
            TimeSpan timeout,
            CancellationToken cancellationToken
        )
        {
            if (
                arguments.Count > 0
                && string.Equals(arguments[0], "ls-remote", StringComparison.Ordinal)
            )
            {
                return Success("1111111111111111111111111111111111111111\trefs/heads/main");
            }

            if (arguments.Contains("checkout", StringComparer.Ordinal))
            {
                CheckoutCount++;
                var root = arguments[1];
                if (createBasePath)
                {
                    Directory.CreateDirectory(Path.Combine(root, "docs"));
                    File.WriteAllText(Path.Combine(root, "docs", "README.md"), "content");
                }
            }

            return Success(string.Empty);
        }

        private static Task<ManifestOperationResult<GitProcessOutput>> Success(string output) =>
            Task.FromResult(
                ManifestOperationResult<GitProcessOutput>.Success(
                    new GitProcessOutput(output, string.Empty)
                )
            );
    }
}
