using System.IO.Abstractions;

namespace Lunapack.Cli.UnitTests;

public sealed class LifecycleHookPlannerTests
{
    [Test]
    public async Task Plan_WhenHooksAreMixed_PreservesDeclarationOrderAndTypedPayloads()
    {
        using var workspace = new TestWorkspace();
        var pack = CreatePack(
            workspace.Path,
            "example",
            new PackManifest.PackHooks
            {
                PreInstall =
                [
                    new PackManifest.PackHook
                    {
                        Type = "instruction",
                        File = "instructions/first.md",
                    },
                    new PackManifest.PackHook { Type = "script", Command = "tool" },
                    new PackManifest.PackHook
                    {
                        Type = "instruction",
                        File = "instructions/last.md",
                    },
                ],
            }
        );
        AddInstructionFiles(pack, "first.md", "last.md");

        var result = Plan([CreateEntry(PackLifecyclePlan.ChangeKind.Install, pack)]);

        await Assert
            .That(
                string.Join(
                    ",",
                    result
                        .RequireValue()
                        .Select(hook =>
                            $"{hook.Position}:{hook.Script.Type}:{hook.Instruction is not null}"
                        )
                )
            )
            .IsEqualTo("1:instruction:True,2:script:False,3:instruction:True");
    }

    [Test]
    public async Task Plan_WhenMultipleScriptsDeclared_PreservesAllInOrder()
    {
        using var workspace = new TestWorkspace();
        var pack = CreatePack(
            workspace.Path,
            "example",
            new PackManifest.PackHooks
            {
                PreInstall =
                [
                    new PackManifest.PackHook { Type = "script", Command = "first" },
                    new PackManifest.PackHook { Type = "script", Command = "second" },
                ],
            }
        );

        var result = Plan([CreateEntry(PackLifecyclePlan.ChangeKind.Install, pack)]);

        await Assert
            .That(string.Join(",", result.RequireValue().Select(hook => hook.Script.Command)))
            .IsEqualTo("first,second");
    }

    [Test]
    public async Task Plan_WhenEventDisabled_SuppressesAllTypedHooks()
    {
        using var workspace = new TestWorkspace();
        var pack = CreatePack(
            workspace.Path,
            "example",
            new PackManifest.PackHooks
            {
                PreInstall =
                [
                    new PackManifest.PackHook { Type = "script", Command = "tool" },
                    new PackManifest.PackHook
                    {
                        Type = "instruction",
                        File = "instructions/setup.md",
                    },
                ],
            }
        );

        var result = Plan([
            CreateEntry(
                PackLifecyclePlan.ChangeKind.Install,
                pack,
                new HashSet<string>(["preInstall"], StringComparer.Ordinal)
            ),
        ]);

        await Assert.That(result.RequireValue()).IsEmpty();
    }

    [Test]
    public async Task Plan_WhenInstallDependencyPrecedesUpdatedRoot_SelectsEventsInPlanOrder()
    {
        using var workspace = new TestWorkspace();
        var dependency = CreatePack(
            workspace.Path,
            "dependency",
            new PackManifest.PackHooks
            {
                PreInstall = [new PackManifest.PackHook { Type = "script", Command = "install" }],
            }
        );
        var root = CreatePack(
            workspace.Path,
            "root",
            new PackManifest.PackHooks
            {
                PreUpdate = [new PackManifest.PackHook { Type = "script", Command = "update" }],
            }
        );

        var result = Plan([
            CreateEntry(PackLifecyclePlan.ChangeKind.Install, dependency),
            CreateEntry(PackLifecyclePlan.ChangeKind.Update, root),
        ]);

        await Assert
            .That(
                string.Join(
                    ",",
                    result
                        .RequireValue()
                        .Select(hook =>
                            $"{hook.Pack.Manifest.Id}:{LifecycleHookPlanner.ToManifestValue(hook.Hook)}"
                        )
                )
            )
            .IsEqualTo("dependency:preInstall,root:preUpdate");
    }

    [Test]
    public async Task Plan_WhenPackUnchangedOrRemoved_EmitsNoHooks()
    {
        using var workspace = new TestWorkspace();
        var pack = CreatePack(
            workspace.Path,
            "example",
            new PackManifest.PackHooks
            {
                PreInstall = [new PackManifest.PackHook { Type = "script", Command = "tool" }],
            }
        );

        var result = Plan([
            CreateEntry(PackLifecyclePlan.ChangeKind.Unchanged, pack),
            CreateEntry(PackLifecyclePlan.ChangeKind.Removed, null),
        ]);

        await Assert.That(result.RequireValue()).IsEmpty();
    }

    [Test]
    public async Task Plan_WhenRemovedPackIsMaterialized_SelectsUninstallEvents()
    {
        using var workspace = new TestWorkspace();
        var pack = CreatePack(
            workspace.Path,
            "example",
            new PackManifest.PackHooks
            {
                PreUninstall = [new PackManifest.PackHook { Type = "script", Command = "before" }],
                PostUninstall = [new PackManifest.PackHook { Type = "script", Command = "after" }],
            }
        );
        var entry = CreateEntry(PackLifecyclePlan.ChangeKind.Removed, pack);
        var plan = new PackLifecyclePlan([entry], [entry], [entry]);
        var parameters = new ResolvedPackParameters(
            new Dictionary<string, PackParameterDefinition>(StringComparer.Ordinal),
            new Dictionary<string, ResolvedPackParameterValue>(StringComparer.Ordinal)
        );
        var planner = new LifecycleHookPlanner(new FileSystem());

        var pre = planner.PlanPreMutation(plan, parameters);
        var post = planner.PlanPostMutation(plan, parameters);

        await Assert
            .That(LifecycleHookPlanner.ToManifestValue(pre.RequireValue().Single().Hook))
            .IsEqualTo("preUninstall");
        await Assert
            .That(LifecycleHookPlanner.ToManifestValue(post.RequireValue().Single().Hook))
            .IsEqualTo("postUninstall");
    }

    [Test]
    public async Task Plan_WhenInstructionsSkipped_DoesNotLoadMissingFileAndRetainsScript()
    {
        using var workspace = new TestWorkspace();
        var pack = CreatePack(
            workspace.Path,
            "example",
            new PackManifest.PackHooks
            {
                PreInstall =
                [
                    new PackManifest.PackHook
                    {
                        Type = "instruction",
                        File = "instructions/missing.md",
                    },
                    new PackManifest.PackHook { Type = "script", Command = "tool" },
                ],
            }
        );

        var result = Plan([CreateEntry(PackLifecyclePlan.ChangeKind.Install, pack)], true);

        await Assert.That(result.IsSuccess).IsTrue();
        await Assert.That(result.RequireValue().Single().IsScript).IsTrue();
    }

    private static ManifestOperationResult<IReadOnlyList<LifecycleHookInvocation>> Plan(
        IReadOnlyList<PackLifecyclePlan.Entry> entries,
        bool skipInstructions = false
    ) =>
        new LifecycleHookPlanner(new FileSystem()).PlanPreMutation(
            new PackLifecyclePlan(entries, entries, []),
            new ResolvedPackParameters(
                new Dictionary<string, PackParameterDefinition>(StringComparer.Ordinal),
                new Dictionary<string, ResolvedPackParameterValue>(StringComparer.Ordinal)
            ),
            skipInstructions
        );

    private static PackLifecyclePlan.Entry CreateEntry(
        PackLifecyclePlan.ChangeKind kind,
        DiscoveredPack? pack,
        IReadOnlySet<string>? disabledHooks = null
    ) => new(kind, pack, null, true, disabledHooks ?? new HashSet<string>(StringComparer.Ordinal));

    private static DiscoveredPack CreatePack(string root, string id, PackManifest.PackHooks hooks)
    {
        var sourcePath = Directory.CreateDirectory(Path.Combine(root, "source", id)).FullName;
        var packPath = Directory.CreateDirectory(Path.Combine(sourcePath, "pack")).FullName;
        return new DiscoveredPack(
            sourcePath,
            packPath,
            new PackManifest
            {
                Id = id,
                Version = "1.0.0",
                Hooks = hooks,
            },
            "local",
            ConfiguredSourceIdentity.CreateLocal(sourcePath)
        );
    }

    private static void AddInstructionFiles(DiscoveredPack pack, params string[] names)
    {
        var directory = Directory.CreateDirectory(Path.Combine(pack.PackDirectory, "instructions"));
        foreach (var name in names)
        {
            File.WriteAllText(Path.Combine(directory.FullName, name), "## Setup\nDetails");
        }
    }
}
