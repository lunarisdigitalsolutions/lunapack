namespace Lunapack.Cli.IntegrationTests.Scenarios.Oci;

public sealed class OciPublishingWorkflowPackTests
{
    private static readonly string _sharedPublisherPackId = "github-publish-changed-packs";
    private static readonly (string PackId, string Target, string WorkflowFile)[] _workflowPacks =
    [
        ("github-oci-publish-workflow", "generic", "publish-lunapacks-oci.yml"),
        ("github-acr-publish-workflow", "acr", "publish-lunapacks-acr.yml"),
        ("github-ghcr-publish-workflow", "ghcr", "publish-lunapacks-ghcr.yml"),
    ];

    [Test]
    public async Task Scenario_InstallableOciPublishingPacks_AllPassPackValidation()
    {
        var repositoryRoot = GetRepositoryRoot();

        var packIds = _workflowPacks.Select(pack => pack.PackId).Append(_sharedPublisherPackId);
        foreach (var packId in packIds)
        {
            var packDirectory = GetPackDirectory(repositoryRoot, packId);
            var validation = await CliProcess.InvokeAsync(packDirectory, "pack", "validate");

            await Assert.That(validation.ExitCode).IsEqualTo(0).Because(Describe(validation));
        }
    }

    [Test]
    public async Task Scenario_InstalledRegistryWorkflowPacks_IncludeSharedPublisher()
    {
        var repositoryRoot = GetRepositoryRoot();

        foreach (var (packId, _, workflowFile) in _workflowPacks)
        {
            using var workspace = new TestWorkspace();
            var initialization = await CliProcess.InvokeAsync(workspace.Path, "init");
            await Assert
                .That(initialization.ExitCode)
                .IsEqualTo(0)
                .Because(Describe(initialization));

            foreach (var sourcePackId in new[] { packId, _sharedPublisherPackId })
            {
                CopyDirectory(
                    GetPackDirectory(repositoryRoot, sourcePackId),
                    Path.Combine(
                        workspace.Path,
                        "source",
                        "platforms",
                        "github",
                        "actions",
                        sourcePackId,
                        "1.0.0"
                    )
                );
            }

            var source = await CliProcess.InvokeAsync(
                workspace.Path,
                "sources",
                "add",
                "local",
                "repository",
                "source"
            );
            await Assert.That(source.ExitCode).IsEqualTo(0).Because(Describe(source));

            var install = await CliProcess.InvokeAsync(
                workspace.Path,
                "install",
                $"{packId}@1.0.0"
            );
            await Assert.That(install.ExitCode).IsEqualTo(0).Because(Describe(install));
            await Assert
                .That(
                    File.Exists(
                        Path.Combine(
                            workspace.Path,
                            ".github",
                            "scripts",
                            "publish-changed-packs.ps1"
                        )
                    )
                )
                .IsTrue();
            await Assert
                .That(
                    File.Exists(Path.Combine(workspace.Path, ".github", "workflows", workflowFile))
                )
                .IsTrue();
        }
    }

    [Test]
    public async Task Scenario_RegistryPublishingWorkflows_UseSharedPowerShellPublisher()
    {
        var repositoryRoot = GetRepositoryRoot();
        var sharedPublisherDirectory = GetPackDirectory(repositoryRoot, _sharedPublisherPackId);
        var sharedPublisherScript = File.ReadAllText(
            Path.Combine(
                sharedPublisherDirectory,
                "targets",
                ".github",
                "scripts",
                "publish-changed-packs.ps1"
            )
        );

        await Assert.That(sharedPublisherScript).Contains("ConvertFrom-Json");
        await Assert.That(sharedPublisherScript).Contains("'pack.yml'");
        await Assert.That(sharedPublisherScript).Contains("& luna publish");
        await Assert.That(sharedPublisherScript).Contains("--target $Target");

        foreach (var (packId, target, workflowFile) in _workflowPacks)
        {
            var workflow = ReadWorkflow(repositoryRoot, packId, workflowFile);
            var manifest = File.ReadAllText(
                Path.Combine(GetPackDirectory(repositoryRoot, packId), "pack.yml")
            );

            await Assert.That(manifest).Contains($"- id: {_sharedPublisherPackId}");
            await Assert
                .That(workflow)
                .Contains(
                    "tj-actions/changed-files@9426d40962ed5378910ee2e21d5f8c6fcbf2dd96 # v47.0.6"
                );
            await Assert.That(workflow).Contains("json: true");
            await Assert.That(workflow).Contains("output_renamed_files_as_deleted_and_added: true");
            await Assert.That(workflow).Contains("fail_on_initial_diff_error: true");
            await Assert.That(workflow).Contains("files: |");
            await Assert.That(workflow).Contains("packs/**");
            await Assert.That(workflow).Contains("shell: pwsh");
            await Assert.That(workflow).Contains(".github/scripts/publish-changed-packs.ps1");
            await Assert.That(workflow).Contains($"-Target {target}");
            await Assert.That(workflow).Contains("-IncludeAllPacks:$publishAllPacks");
            await Assert.That(workflow).DoesNotContain("python3");
            await Assert.That(workflow).DoesNotContain("find_changed_packs_");
            await Assert.That(workflow).DoesNotContain("luna publish");
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
            var (packId, _, workflowFile) in _workflowPacks.Where(pack =>
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

    private static void CopyDirectory(string sourceDirectory, string destinationDirectory)
    {
        Directory.CreateDirectory(destinationDirectory);
        foreach (var sourceFile in Directory.EnumerateFiles(sourceDirectory))
        {
            File.Copy(sourceFile, Path.Combine(destinationDirectory, Path.GetFileName(sourceFile)));
        }

        foreach (var sourceSubdirectory in Directory.EnumerateDirectories(sourceDirectory))
        {
            CopyDirectory(
                sourceSubdirectory,
                Path.Combine(destinationDirectory, Path.GetFileName(sourceSubdirectory))
            );
        }
    }

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
