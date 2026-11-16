namespace Lunapack.Cli.IntegrationTests.Scenarios.Oci;

public sealed class OciPublishingWorkflowPackTests
{
    private static readonly (
        string PackId,
        string Target,
        string WorkflowFile,
        string ChangeDetectionScript
    )[] _workflowPacks =
    [
        (
            "github-oci-publish-workflow",
            "generic",
            "publish-lunapacks-oci.yml",
            "find_changed_packs_oci.py"
        ),
        (
            "github-acr-publish-workflow",
            "acr",
            "publish-lunapacks-acr.yml",
            "find_changed_packs_acr.py"
        ),
        (
            "github-ghcr-publish-workflow",
            "ghcr",
            "publish-lunapacks-ghcr.yml",
            "find_changed_packs_ghcr.py"
        ),
    ];

    [Test]
    public async Task Scenario_InstallableOciPublishingPacks_AllPassPackValidation()
    {
        var repositoryRoot = GetRepositoryRoot();

        foreach (var (packId, _, _, _) in _workflowPacks)
        {
            var packDirectory = GetPackDirectory(repositoryRoot, packId);
            var validation = await CliProcess.InvokeAsync(packDirectory, "pack", "validate");

            await Assert.That(validation.ExitCode).IsEqualTo(0).Because(Describe(validation));
        }
    }

    [Test]
    public async Task Scenario_RegistryPublishingWorkflows_UseChangedPackCliPublishing()
    {
        var repositoryRoot = GetRepositoryRoot();

        foreach (var (packId, target, workflowFile, changeDetectionScript) in _workflowPacks)
        {
            var workflow = ReadWorkflow(repositoryRoot, packId, workflowFile);

            await Assert
                .That(workflow)
                .Contains($"python3 .github/scripts/{changeDetectionScript}");
            await Assert.That(workflow).Contains($"luna publish \"$pack\" --target {target}");
            await Assert.That(workflow).DoesNotContain("LUNAPACK_OCI_SOURCE");
            await Assert.That(workflow).DoesNotContain("LUNAPACK_OCI_USERNAME");
            await Assert.That(workflow).DoesNotContain("LUNAPACK_OCI_PASSWORD");
            await Assert.That(workflow).DoesNotContain("AZURE_CLIENT_SECRET");
            await Assert.That(workflow).DoesNotContain("secrets.");
            await Assert.That(workflow).DoesNotContain("pack.yml");
            await Assert.That(workflow).DoesNotContain("--id");
            await Assert.That(workflow).DoesNotContain("--version");
            await Assert.That(workflow).DoesNotContain("oras");
            await Assert.That(workflow).DoesNotContain("docker");
            await Assert.That(workflow).DoesNotContain("registry/v2/");
            await Assert.That(workflow).DoesNotContain("curl ");
            await Assert.That(workflow).DoesNotContain("wget ");
            await Assert.That(workflow).DoesNotContain("gh api");
        }
    }

    [Test]
    public async Task Scenario_AcrPublishingWorkflow_UsesGitHubOidc()
    {
        var repositoryRoot = GetRepositoryRoot();
        var workflow = ReadWorkflow(
            repositoryRoot,
            "github-acr-publish-workflow",
            "publish-lunapacks-acr.yml"
        );

        await Assert.That(workflow).Contains("id-token: write");
        await Assert.That(workflow).Contains("azure/login@");
        await Assert.That(workflow).Contains("vars.AZURE_CLIENT_ID");
        await Assert.That(workflow).Contains("vars.AZURE_TENANT_ID");
        await Assert.That(workflow).Contains("vars.AZURE_SUBSCRIPTION_ID");
    }

    [Test]
    public async Task Scenario_GenericAndGhcrPublishingWorkflows_UsePreparedRunnerCredentials()
    {
        var repositoryRoot = GetRepositoryRoot();

        foreach (
            var (packId, _, workflowFile, _) in _workflowPacks.Where(pack =>
                pack.Target is "generic" or "ghcr"
            )
        )
        {
            var workflow = ReadWorkflow(repositoryRoot, packId, workflowFile);

            await Assert.That(workflow).Contains("self-hosted");
            await Assert.That(workflow).Contains("linux");
            await Assert.That(workflow).DoesNotContain("GITHUB_TOKEN");
        }
    }

    private static string GetPackDirectory(string repositoryRoot, string packId) =>
        Path.Combine(
            repositoryRoot,
            "projects",
            "packs",
            "platforms",
            "github",
            "actions",
            packId,
            "1.0.0"
        );

    private static string ReadWorkflow(string repositoryRoot, string packId, string workflowFile) =>
        File.ReadAllText(
            Path.Combine(
                GetPackDirectory(repositoryRoot, packId),
                "targets",
                ".github",
                "workflows",
                workflowFile
            )
        );

    private static string GetRepositoryRoot()
    {
        for (
            DirectoryInfo? directory = new(AppContext.BaseDirectory);
            directory is not null;
            directory = directory.Parent
        )
        {
            if (
                File.Exists(Path.Combine(directory.FullName, "global.json"))
                && Directory.Exists(Path.Combine(directory.FullName, "projects", "packs"))
            )
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException("Unable to find the LunaPack repository root.");
    }

    private static string Describe(CliResult result) =>
        $"{result.StandardOutput}{Environment.NewLine}{result.StandardError}";
}
