using System.IO.Abstractions;

namespace Lunapack.Cli.UnitTests;

public sealed class LifecycleHookAuthorizationTests
{
    [Test]
    public async Task Plan_WhenInstallHasScripts_EmitsOrderedPreAndPostHooks()
    {
        var pack = CreatePack(
            new PackManifest.PackHooks
            {
                PreInstall = [new PackManifest.PackHook { Type = "script", Command = "pre" }],
                PostInstall = [new PackManifest.PackHook { Type = "script", Command = "post" }],
            }
        );
        var plan = new PackLifecyclePlan(
            [
                new PackLifecyclePlan.Entry(
                    PackLifecyclePlan.ChangeKind.Install,
                    pack,
                    null,
                    true,
                    new HashSet<string>(StringComparer.Ordinal)
                ),
            ],
            [
                new PackLifecyclePlan.Entry(
                    PackLifecyclePlan.ChangeKind.Install,
                    pack,
                    null,
                    true,
                    new HashSet<string>(StringComparer.Ordinal)
                ),
            ],
            [
                new PackLifecyclePlan.Entry(
                    PackLifecyclePlan.ChangeKind.Install,
                    pack,
                    null,
                    true,
                    new HashSet<string>(StringComparer.Ordinal)
                ),
            ]
        );
        var planner = new LifecycleHookPlanner(new FileSystem());

        var parameters = CreateParameters();
        var preHooks = planner.PlanPreMutation(plan, parameters);
        var postHooks = planner.PlanPostMutation(plan, parameters);

        await Assert
            .That(preHooks.RequireValue().Single().Hook)
            .IsEqualTo(LifecycleHook.PreInstall);
        await Assert
            .That(postHooks.RequireValue().Single().Hook)
            .IsEqualTo(LifecycleHook.PostInstall);
    }

    [Test]
    public async Task Plan_WhenScriptArgumentUsesParameter_RendersArgument()
    {
        var pack = CreatePack(
            new PackManifest.PackHooks
            {
                PreInstall =
                [
                    new PackManifest.PackHook
                    {
                        Type = "script",
                        Command = "tool",
                        Arguments = ["--company", "{{ companyName }}"],
                    },
                ],
            }
        );
        var entry = new PackLifecyclePlan.Entry(
            PackLifecyclePlan.ChangeKind.Install,
            pack,
            null,
            true,
            new HashSet<string>(StringComparer.Ordinal)
        );
        var plan = new PackLifecyclePlan([entry], [entry], [entry]);

        var result = new LifecycleHookPlanner(new FileSystem()).PlanPreMutation(
            plan,
            CreateParameters()
        );

        await Assert
            .That(result.RequireValue().Single().Arguments)
            .IsEquivalentTo(["--company", "Lunaris Digital Solutions"]);
    }

    [Test]
    public async Task Plan_WhenScriptArgumentUsesUnknownParameter_ReturnsFailure()
    {
        var pack = CreatePack(
            new PackManifest.PackHooks
            {
                PreInstall =
                [
                    new PackManifest.PackHook
                    {
                        Type = "script",
                        Command = "tool",
                        Arguments = ["{{ unknown }}"],
                    },
                ],
            }
        );
        var entry = new PackLifecyclePlan.Entry(
            PackLifecyclePlan.ChangeKind.Install,
            pack,
            null,
            true,
            new HashSet<string>(StringComparer.Ordinal)
        );
        var plan = new PackLifecyclePlan([entry], [entry], [entry]);

        var result = new LifecycleHookPlanner(new FileSystem()).PlanPreMutation(
            plan,
            CreateParameters()
        );

        await Assert.That(result.IsSuccess).IsFalse();
    }

    [Test]
    public async Task AuthorizeAsync_WhenSkipOrRun_DoesNotPrompt()
    {
        using var workspace = new TestWorkspace();
        var fileSystem = new FileSystem();
        var confirmer = new RecordingConfirmer();
        var authorizer = new LifecycleHookAuthorizer(
            new UserSettingsStore(fileSystem, workspace.Path),
            new TrustPolicy(fileSystem),
            new LifecycleCommandResolver(fileSystem),
            confirmer
        );
        var invocation = CreateInvocation(workspace.Path, Environment.ProcessPath!);
        var configuration = CreateConfiguration();

        var skipped = await authorizer.AuthorizeAsync(
            workspace.Path,
            configuration,
            ScriptExecutionMode.Skip,
            [invocation]
        );
        var run = await authorizer.AuthorizeAsync(
            workspace.Path,
            configuration,
            ScriptExecutionMode.Run,
            [invocation]
        );

        await Assert.That(skipped.RequireValue()).IsEmpty();
        await Assert.That(run.RequireValue()).Count().IsEqualTo(1);
        await Assert.That(confirmer.CallCount).IsEqualTo(0);
    }

    [Test]
    public async Task AuthorizeAsync_WhenHooksMixed_AppliesScriptModeWithoutSuppressingInstructions()
    {
        using var workspace = new TestWorkspace();
        var fileSystem = new FileSystem();
        var confirmer = new RecordingConfirmer();
        var authorizer = new LifecycleHookAuthorizer(
            new UserSettingsStore(fileSystem, workspace.Path),
            new TrustPolicy(fileSystem),
            new LifecycleCommandResolver(fileSystem),
            confirmer
        );
        var script = CreateInvocation(workspace.Path, Environment.ProcessPath!) with
        {
            Position = 2,
        };
        var invocations = new[]
        {
            CreateInstructionInvocation(workspace.Path, 1),
            script,
            CreateInstructionInvocation(workspace.Path, 3),
        };

        var skipped = await authorizer.AuthorizeAsync(
            workspace.Path,
            CreateConfiguration(),
            ScriptExecutionMode.Skip,
            invocations
        );
        var run = await authorizer.AuthorizeAsync(
            workspace.Path,
            CreateConfiguration(),
            ScriptExecutionMode.Run,
            invocations
        );

        await Assert
            .That(string.Join(",", skipped.RequireValue().Select(hook => hook.Invocation.Position)))
            .IsEqualTo("1,3");
        await Assert
            .That(string.Join(",", run.RequireValue().Select(hook => hook.Invocation.Position)))
            .IsEqualTo("1,2,3");
        await Assert.That(confirmer.CallCount).IsEqualTo(0);
    }

    [Test]
    public async Task AuthorizeAsync_WhenPromptTrustIsAbsentAndConfirmationDeclined_SkipsHook()
    {
        using var workspace = new TestWorkspace();
        var fileSystem = new FileSystem();
        var confirmer = new RecordingConfirmer();
        var authorizer = new LifecycleHookAuthorizer(
            new UserSettingsStore(fileSystem, workspace.Path),
            new TrustPolicy(fileSystem),
            new LifecycleCommandResolver(fileSystem),
            confirmer
        );

        var result = await authorizer.AuthorizeAsync(
            workspace.Path,
            CreateConfiguration(),
            ScriptExecutionMode.Prompt,
            [CreateInvocation(workspace.Path, Environment.ProcessPath!)]
        );

        await Assert.That(result.RequireValue()).IsEmpty();
        await Assert.That(confirmer.CallCount).IsEqualTo(1);
    }

    [Test]
    public async Task AuthorizeAsync_WhenScriptDeclined_PreservesInstructionEntry()
    {
        using var workspace = new TestWorkspace();
        var fileSystem = new FileSystem();
        var confirmer = new RecordingConfirmer();
        var authorizer = new LifecycleHookAuthorizer(
            new UserSettingsStore(fileSystem, workspace.Path),
            new TrustPolicy(fileSystem),
            new LifecycleCommandResolver(fileSystem),
            confirmer
        );

        var result = await authorizer.AuthorizeAsync(
            workspace.Path,
            CreateConfiguration(),
            ScriptExecutionMode.Prompt,
            [
                CreateInstructionInvocation(workspace.Path, 1),
                CreateInvocation(workspace.Path, Environment.ProcessPath!) with
                {
                    Position = 2,
                },
            ]
        );

        await Assert.That(result.RequireValue()).Count().IsEqualTo(1);
        await Assert.That(result.RequireValue().Single().Invocation.IsInstruction).IsTrue();
        await Assert.That(confirmer.CallCount).IsEqualTo(1);
    }

    [Test]
    public async Task Confirm_WhenConfirmationIsUnavailable_WarnsThatHookIsSkipped()
    {
        using var workspace = new TestWorkspace();
        var ansiConsole = new Spectre.Console.Testing.TestConsole();
        ansiConsole.Profile.Width = 500;
        var confirmer = new ConsoleLifecycleHookConfirmer(
            new CliConsole(ansiConsole, CliLogLevel.Info)
        );
        var invocation = new ResolvedLifecycleHookInvocation(
            CreateInvocation(workspace.Path, Environment.ProcessPath!),
            Environment.ProcessPath!
        );

        var confirmed = confirmer.Confirm(invocation);

        await Assert.That(confirmed).IsFalse();
        await Assert.That(ansiConsole.Output).Contains("was not authorized and will be skipped");
    }

    [Test]
    public async Task AuthorizeAsync_WhenPromptSourceTrustMatches_DoesNotPrompt()
    {
        using var workspace = new TestWorkspace();
        var fileSystem = new FileSystem();
        var confirmer = new RecordingConfirmer();
        var userSettingsStore = new UserSettingsStore(fileSystem, workspace.Path);
        var invocation = CreateInvocation(workspace.Path, Environment.ProcessPath!);
        var saved = await userSettingsStore.SaveAsync(
            new UserSettings
            {
                Global = new UserTrust { Sources = [invocation.Pack.SourceIdentity] },
            }
        );
        var authorizer = new LifecycleHookAuthorizer(
            userSettingsStore,
            new TrustPolicy(fileSystem),
            new LifecycleCommandResolver(fileSystem),
            confirmer
        );

        await Assert.That(saved.IsSuccess).IsTrue();
        var result = await authorizer.AuthorizeAsync(
            workspace.Path,
            CreateConfiguration(),
            ScriptExecutionMode.Prompt,
            [invocation]
        );

        await Assert.That(result.RequireValue()).Count().IsEqualTo(1);
        await Assert.That(confirmer.CallCount).IsEqualTo(0);
    }

    [Test]
    public async Task Format_WhenHookUsesPackedFile_DoesNotRevealSnapshotPathInArguments()
    {
        using var workspace = new TestWorkspace();
        var pack = CreatePack(null, workspace.Path);
        var hookPath = Path.Combine(pack.PackDirectory, "scripts", "setup.ps1");
        Directory.CreateDirectory(Path.GetDirectoryName(hookPath)!);
        File.WriteAllText(hookPath, "Write-Output setup");
        var script = new PackManifest.PackHook
        {
            Type = "script",
            File = "scripts/setup.ps1",
            Runner = Environment.ProcessPath,
            Arguments = ["two words", "&"],
        };
        var packedFile = PackedHookFile.Resolve(new FileSystem(), pack, script.File).RequireValue();
        var resolved = new ResolvedLifecycleHookInvocation(
            new LifecycleHookInvocation(pack, LifecycleHook.PreInstall, script, packedFile),
            Environment.ProcessPath!
        );

        var formatted = LifecycleHookConfirmationFormatter.Format(resolved);

        await Assert.That(formatted).Contains("Packed file: scripts/setup.ps1");
        await Assert.That(formatted).Contains("Arguments: \"two words\" &");
        await Assert.That(formatted).DoesNotContain(pack.PackDirectory);
    }

    private static ProjectConfiguration CreateConfiguration() =>
        new()
        {
            Sources = [new ProjectConfiguration.LocalSource { Name = "local", Path = "source" }],
        };

    private static ResolvedPackParameters CreateParameters() =>
        new(
            new Dictionary<string, PackParameterDefinition>(StringComparer.Ordinal)
            {
                ["companyName"] = new(PackParameterType.String, true, []),
            },
            new Dictionary<string, ResolvedPackParameterValue>(StringComparer.Ordinal)
            {
                ["companyName"] = new(PackParameterType.String, "Lunaris Digital Solutions", false),
            }
        );

    private static LifecycleHookInvocation CreateInvocation(
        string projectDirectory,
        string command
    ) =>
        new(
            CreatePack(null, projectDirectory),
            LifecycleHook.PreInstall,
            new PackManifest.PackHook { Type = "script", Command = command },
            null
        );

    private static LifecycleHookInvocation CreateInstructionInvocation(
        string projectDirectory,
        int position
    )
    {
        var packedFile = new PackedHookFile(
            "instructions/setup.md",
            Path.Combine(projectDirectory, "setup.md"),
            "HASH"
        );
        return new LifecycleHookInvocation(
            CreatePack(null, projectDirectory),
            LifecycleHook.PreInstall,
            new PackManifest.PackHook { Type = "instruction", File = "instructions/setup.md" },
            packedFile,
            position,
            new PreparedInstruction(
                packedFile,
                false,
                new InstructionDocument(string.Empty, [new InstructionStep(1, null, null, "Setup")])
            )
        );
    }

    private static DiscoveredPack CreatePack(PackManifest.PackHooks? hooks, string? root = null)
    {
        var sourcePath = root is null
            ? Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))
            : Path.Combine(root, "source");
        var packPath = Path.Combine(sourcePath, "pack");
        Directory.CreateDirectory(packPath);
        var sourceIdentity = ConfiguredSourceIdentity.CreateLocal(
            CanonicalProjectPath.Resolve(new FileSystem(), sourcePath).RequireValue()
        );
        return new DiscoveredPack(
            sourcePath,
            packPath,
            new PackManifest
            {
                Id = "example",
                Version = "1.0.0",
                Hooks = hooks,
            },
            "local",
            sourceIdentity
        );
    }

    private sealed class RecordingConfirmer : ILifecycleHookConfirmer
    {
        public int CallCount { get; private set; }

        public bool Confirm(ResolvedLifecycleHookInvocation invocation)
        {
            CallCount++;
            return false;
        }
    }
}
