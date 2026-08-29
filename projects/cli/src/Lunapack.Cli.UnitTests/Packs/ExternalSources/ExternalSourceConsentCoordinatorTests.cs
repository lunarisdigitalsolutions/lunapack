using Lunapack.Cli.Packs.ExternalSources;
using Lunapack.Cli.Project;
using Lunapack.Cli.Sources;

namespace Lunapack.Cli.UnitTests.Packs.ExternalSources;

public sealed class ExternalSourceConsentCoordinatorTests
{
    [Test]
    public async Task ApproveAsync_WhenApprovalDeclined_ReturnsFailureWithoutCandidate()
    {
        var coordinator = new ExternalSourceConsentCoordinator(
            new StubApprover(false),
            new StubIdentifierPrompter([])
        );

        var result = await coordinator.ApproveAsync(CreatePlan(), CreateConfiguration(), false);

        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That(result.Value).IsNull();
    }

    [Test]
    public async Task ApproveAsync_WhenConflictResolved_RepeatsUntilIdentifierValidAndUnused()
    {
        var prompts = new StubIdentifierPrompter(["invalid name", "configured", "standards"]);
        var coordinator = new ExternalSourceConsentCoordinator(new StubApprover(true), prompts);
        var configuration = new ProjectConfiguration
        {
            SchemaVersion = 1,
            Sources =
            [
                new ProjectConfiguration.LocalSource { Name = "configured", Path = "packs" },
            ],
        };

        var result = await coordinator.ApproveAsync(
            CreatePlan(identifierConflict: "local:packs"),
            configuration,
            false
        );

        await Assert.That(result.Value?.CandidateConfiguration.Sources.Count).IsEqualTo(2);
        await Assert
            .That(result.Value?.Requirements.Mappings.Single().WorkspaceSourceName)
            .IsEqualTo("standards");
        await Assert.That(prompts.CallCount).IsEqualTo(3);
    }

    [Test]
    public async Task ApproveAsync_WhenAcceptSourcesHasConflict_ReturnsManualConfigurationFailure()
    {
        var coordinator = new ExternalSourceConsentCoordinator(
            new StubApprover(true),
            new StubIdentifierPrompter([])
        );

        var result = await coordinator.ApproveAsync(
            CreatePlan(identifierConflict: "local:packs"),
            CreateConfiguration(),
            true
        );

        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That(result.Error).Contains("luna sources add git");
    }

    [Test]
    public async Task ApproveAsync_WhenAcceptSourcesIsConflictFree_AddsCandidateWithoutPrompt()
    {
        var approver = new StubApprover(false);
        var coordinator = new ExternalSourceConsentCoordinator(
            approver,
            new StubIdentifierPrompter([])
        );

        var result = await coordinator.ApproveAsync(CreatePlan(), CreateConfiguration(), true);

        await Assert
            .That(result.Value?.CandidateConfiguration.Sources.Single().Name)
            .IsEqualTo("upstream");
        await Assert.That(approver.CallCount).IsEqualTo(0);
    }

    private static ExternalSourceRequirementPlan CreatePlan(string? identifierConflict = null)
    {
        var fingerprint = SourceIdentityNormalizer
            .CreateGit("https://github.com/example/standards.git", "refs/heads/main", null)
            .RequireValue();
        var group = new ExternalSourceRequirementGroup(
            fingerprint,
            new ProjectConfiguration.GitSource
            {
                Name = "upstream",
                Url = "https://github.com/example/standards.git",
                Ref = "refs/heads/main",
            },
            [new ExternalSourceRequirementUse("pack", "1.0.0", "upstream", null, 1)],
            "upstream",
            false,
            identifierConflict
        );
        return new ExternalSourceRequirementPlan(
            [group],
            [new ExternalSourceAliasMapping("pack", "1.0.0", "upstream", "upstream", fingerprint)]
        );
    }

    private static ProjectConfiguration CreateConfiguration() => new() { SchemaVersion = 1 };

    private sealed class StubApprover(bool result) : IExternalSourceApprover
    {
        public int CallCount { get; private set; }

        public Task<bool> ApproveAsync(
            IReadOnlyList<ExternalSourceRequirementGroup> sources,
            CancellationToken cancellationToken
        )
        {
            CallCount++;
            return Task.FromResult(result);
        }
    }

    private sealed class StubIdentifierPrompter(IEnumerable<string?> values)
        : IExternalSourceIdentifierPrompter
    {
        private readonly Queue<string?> _values = new(values);

        public int CallCount { get; private set; }

        public Task<string?> PromptAsync(
            ExternalSourceRequirementGroup source,
            string conflictingIdentifier,
            CancellationToken cancellationToken
        )
        {
            CallCount++;
            return Task.FromResult(_values.Dequeue());
        }
    }
}
