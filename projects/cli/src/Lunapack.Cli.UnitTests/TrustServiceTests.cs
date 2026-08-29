using System.IO.Abstractions;
using Lunapack.Cli.Application.Paths;
using Lunapack.Cli.Project;
using Lunapack.Cli.Trust;
using Spectre.Console;

namespace Lunapack.Cli.UnitTests;

public sealed class TrustServiceTests
{
    [Test]
    public async Task DenyScripts_WhenProject_WritesOnlyPortableDenialWithoutConfirmation()
    {
        using var workspace = new TestWorkspace();
        Directory.CreateDirectory(Path.Combine(workspace.Path, "source"));
        var confirmer = new AcceptingTrustConfirmer();
        var service = await CreateServiceAsync(workspace, confirmer);

        var denied = await service.DenyScriptsAsync(workspace.Path, TrustScope.Project);
        var state = await workspace.StateStore.LoadAsync(workspace.Path);

        await Assert.That(denied.IsSuccess).IsTrue();
        await Assert.That(state.RequireValue().Configuration.Trust.Deny?.Scripts).IsTrue();
        await Assert.That(confirmer.Warning).IsNull();
        await Assert.That(File.Exists(CreateSettingsStore(workspace).SettingsPath)).IsFalse();
    }

    [Test]
    [Arguments((int)TrustScope.LocalUser)]
    [Arguments((int)TrustScope.GlobalUser)]
    public async Task DenyScripts_WhenUserScope_SetsDenialIdempotentlyWithoutConfirmation(
        int scopeValue
    )
    {
        var scope = (TrustScope)scopeValue;
        using var workspace = new TestWorkspace();
        Directory.CreateDirectory(Path.Combine(workspace.Path, "source"));
        var confirmer = new AcceptingTrustConfirmer();
        var service = await CreateServiceAsync(workspace, confirmer);

        var first = await service.DenyScriptsAsync(workspace.Path, scope);
        var second = await service.DenyScriptsAsync(workspace.Path, scope);
        var listing = await service.ListAsync(workspace.Path, scope);

        await Assert.That(first.IsSuccess).IsTrue();
        await Assert.That(second.IsSuccess).IsTrue();
        await Assert.That(listing.RequireValue().ScriptsDenied).IsTrue();
        await Assert.That(confirmer.Warning).IsNull();
    }

    [Test]
    public async Task ResetScriptDenial_WhenConfirmed_PreservesRetainedGrants()
    {
        using var workspace = new TestWorkspace();
        Directory.CreateDirectory(Path.Combine(workspace.Path, "source"));
        var confirmer = new AcceptingTrustConfirmer();
        var service = await CreateServiceAsync(workspace, confirmer);
        await service.TrustSourcesAsync(workspace.Path, ["local"], TrustScope.LocalUser);
        await service.DenyScriptsAsync(workspace.Path, TrustScope.LocalUser);

        var reset = await service.ResetScriptDenialAsync(workspace.Path, TrustScope.LocalUser);
        var listing = await service.ListAsync(workspace.Path, TrustScope.LocalUser);

        await Assert.That(reset.IsSuccess).IsTrue();
        await Assert.That(listing.RequireValue().ScriptsDenied).IsFalse();
        await Assert.That(listing.RequireValue().Sources).Count().IsEqualTo(1);
        await Assert
            .That(confirmer.Warning)
            .Contains("reactivate retained source and pack trust grants");
        await Assert.That(confirmer.Warning).Contains("Scope: local user");
    }

    [Test]
    [Arguments((int)TrustScope.LocalUser)]
    [Arguments((int)TrustScope.Project)]
    [Arguments((int)TrustScope.GlobalUser)]
    public async Task ResetScriptDenial_WhenConfirmed_RemovesOnlySelectedScope(int scopeValue)
    {
        var scope = (TrustScope)scopeValue;
        using var workspace = new TestWorkspace();
        Directory.CreateDirectory(Path.Combine(workspace.Path, "source"));
        var service = await CreateServiceAsync(workspace, new AcceptingTrustConfirmer());
        await service.TrustSourcesAsync(workspace.Path, ["local"], scope);
        await service.DenyScriptsAsync(workspace.Path, scope);

        var reset = await service.ResetScriptDenialAsync(workspace.Path, scope);
        var listing = await service.ListAsync(workspace.Path, scope);

        await Assert.That(reset.IsSuccess).IsTrue();
        await Assert.That(listing.RequireValue().ScriptsDenied).IsFalse();
        var retainedGrantCount =
            scope is TrustScope.Project
                ? listing.RequireValue().ProjectSourceDeclarations.Count
                : listing.RequireValue().Sources.Count;
        await Assert.That(retainedGrantCount).IsEqualTo(1);
    }

    [Test]
    public async Task ResetScriptDenial_WhenDeclined_PreservesDenial()
    {
        using var workspace = new TestWorkspace();
        Directory.CreateDirectory(Path.Combine(workspace.Path, "source"));
        var acceptingService = await CreateServiceAsync(workspace, new AcceptingTrustConfirmer());
        await acceptingService.DenyScriptsAsync(workspace.Path, TrustScope.GlobalUser);
        var service = new TrustService(
            workspace.FileSystem,
            workspace.StateStore,
            CreateSettingsStore(workspace),
            new RejectingTrustConfirmer()
        );

        var reset = await service.ResetScriptDenialAsync(workspace.Path, TrustScope.GlobalUser);
        var listing = await service.ListAsync(workspace.Path, TrustScope.GlobalUser);

        await Assert.That(reset.IsSuccess).IsFalse();
        await Assert.That(listing.RequireValue().ScriptsDenied).IsTrue();
    }

    [Test]
    public async Task Command_List_WhenDeniedWithRetainedGrant_ReportsBothEntries()
    {
        using var workspace = new TestWorkspace();
        Directory.CreateDirectory(Path.Combine(workspace.Path, "source"));
        var output = new StringWriter();
        var ansiConsole = AnsiConsole.Create(
            new AnsiConsoleSettings
            {
                Ansi = AnsiSupport.No,
                ColorSystem = ColorSystemSupport.NoColors,
                Out = new AnsiConsoleOutput(output),
            }
        );
        var application = new CliApplication(
            workspace.FileSystem,
            ansiConsole,
            trustConfirmer: new AcceptingTrustConfirmer(),
            userSettingsStore: CreateSettingsStore(workspace)
        );
        await application.RunAsync(["init"], workspace.Path);
        await application.RunAsync(["sources", "add", "local", "local", "source"], workspace.Path);
        await application.RunAsync(["trust", "source", "local"], workspace.Path);
        await application.RunAsync(["trust", "scripts", "deny"], workspace.Path);

        var exitCode = await application.RunAsync(["trust", "list"], workspace.Path);
        var text = output.ToString().ReplaceLineEndings(string.Empty);

        await Assert.That(exitCode).IsEqualTo(0);
        await Assert.That(text).Contains("local-user script denial - scripts: denied");
        await Assert.That(text).Contains("local-user source - identity: local");
    }

    [Test]
    [Arguments("deny")]
    [Arguments("reset")]
    public async Task ScriptsCommand_WhenProjectAndGlobalCombined_Fails(string operation)
    {
        using var workspace = new TestWorkspace();
        var application = new CliApplication(
            workspace.FileSystem,
            TestConsole.CreateAnsiConsole(),
            trustConfirmer: new AcceptingTrustConfirmer(),
            userSettingsStore: CreateSettingsStore(workspace)
        );
        await application.RunAsync(["init"], workspace.Path);

        var exitCode = await application.RunAsync(
            ["trust", "scripts", operation, "--project", "--global"],
            workspace.Path
        );

        await Assert.That(exitCode).IsEqualTo(1);
    }

    [Test]
    public async Task TrustSources_WhenConfirmed_AddsExactLocalUserIdentitiesOnce()
    {
        using var workspace = new TestWorkspace();
        var sourceDirectory = Directory
            .CreateDirectory(Path.Combine(workspace.Path, "source"))
            .FullName;
        var confirmer = new AcceptingTrustConfirmer();
        var service = await CreateServiceAsync(workspace, confirmer);

        var trusted = await service.TrustSourcesAsync(
            workspace.Path,
            ["local", "local"],
            TrustScope.LocalUser
        );
        var settings = await CreateSettingsStore(workspace).LoadAsync();

        await Assert.That(trusted.IsSuccess).IsTrue();
        var projectTrust = settings.RequireValue().Projects.Values.Single();
        await Assert.That(projectTrust.Sources).Count().IsEqualTo(1);
        await Assert
            .That(projectTrust.Sources.Single().Path)
            .IsEqualTo(
                CanonicalProjectPath.Resolve(new FileSystem(), sourceDirectory).RequireValue()
            );
        await Assert.That(confirmer.Warning).Contains("credentials");
    }

    [Test]
    public async Task TrustSources_WhenAnySourceUnknown_WritesNothingAndDoesNotConfirm()
    {
        using var workspace = new TestWorkspace();
        Directory.CreateDirectory(Path.Combine(workspace.Path, "source"));
        var confirmer = new AcceptingTrustConfirmer();
        var service = await CreateServiceAsync(workspace, confirmer);

        var trusted = await service.TrustSourcesAsync(
            workspace.Path,
            ["local", "missing"],
            TrustScope.LocalUser
        );

        await Assert.That(trusted.IsSuccess).IsFalse();
        await Assert.That(confirmer.Warning).IsNull();
        await Assert.That(File.Exists(CreateSettingsStore(workspace).SettingsPath)).IsFalse();
    }

    [Test]
    public async Task TrustPacks_WhenGlobal_AddsBarePackIdentitiesOnce()
    {
        using var workspace = new TestWorkspace();
        Directory.CreateDirectory(Path.Combine(workspace.Path, "source"));
        var service = await CreateServiceAsync(workspace, new AcceptingTrustConfirmer());

        var trusted = await service.TrustPacksAsync(
            workspace.Path,
            ["sdk", "quality", "sdk"],
            "local",
            TrustScope.GlobalUser
        );
        var settings = await CreateSettingsStore(workspace).LoadAsync();

        await Assert.That(trusted.IsSuccess).IsTrue();
        await Assert
            .That(settings.RequireValue().Global.Packs.Select(pack => pack.Id))
            .IsEquivalentTo(["sdk", "quality"]);
        await Assert.That(settings.RequireValue().Projects).IsEmpty();
    }

    [Test]
    public async Task TrustSources_WhenProject_AddsDeclarationAndExactAcknowledgement()
    {
        using var workspace = new TestWorkspace();
        var sourceDirectory = Directory
            .CreateDirectory(Path.Combine(workspace.Path, "source"))
            .FullName;
        var service = await CreateServiceAsync(workspace, new AcceptingTrustConfirmer());

        var trusted = await service.TrustSourcesAsync(
            workspace.Path,
            ["local"],
            TrustScope.Project
        );
        var state = await workspace.StateStore.LoadAsync(workspace.Path);
        var settings = await CreateSettingsStore(workspace).LoadAsync();

        await Assert.That(trusted.IsSuccess).IsTrue();
        await Assert
            .That(state.RequireValue().Configuration.Trust.Sources)
            .IsEquivalentTo(["local"]);
        await Assert
            .That(
                settings
                    .RequireValue()
                    .Projects.Values.Single()
                    .Acknowledgements.Sources.Single()
                    .Path
            )
            .IsEqualTo(
                CanonicalProjectPath.Resolve(new FileSystem(), sourceDirectory).RequireValue()
            );
    }

    [Test]
    public async Task TrustPacks_WhenProject_AddsDeclarationAndExactAcknowledgement()
    {
        using var workspace = new TestWorkspace();
        var sourceDirectory = Directory
            .CreateDirectory(Path.Combine(workspace.Path, "source"))
            .FullName;
        var service = await CreateServiceAsync(workspace, new AcceptingTrustConfirmer());

        var trusted = await service.TrustPacksAsync(
            workspace.Path,
            ["sdk"],
            "local",
            TrustScope.Project
        );
        var state = await workspace.StateStore.LoadAsync(workspace.Path);
        var settings = await CreateSettingsStore(workspace).LoadAsync();

        await Assert.That(trusted.IsSuccess).IsTrue();
        var declaration = state.RequireValue().Configuration.Trust.Packs.Single();
        await Assert.That(declaration.Id).IsEqualTo("sdk");
        await Assert.That(declaration.Source).IsEqualTo("local");
        var acknowledgement = settings
            .RequireValue()
            .Projects.Values.Single()
            .Acknowledgements.Packs.Single();
        await Assert.That(acknowledgement.Id).IsEqualTo("sdk");
        await Assert
            .That(acknowledgement.Source.Path)
            .IsEqualTo(
                CanonicalProjectPath.Resolve(new FileSystem(), sourceDirectory).RequireValue()
            );
    }

    [Test]
    public async Task TrustPacks_WhenVersionQualified_WritesNothingAndDoesNotConfirm()
    {
        using var workspace = new TestWorkspace();
        var confirmer = new AcceptingTrustConfirmer();
        var service = await CreateServiceAsync(workspace, confirmer);

        var trusted = await service.TrustPacksAsync(
            workspace.Path,
            ["sdk@2.0.0"],
            "local",
            TrustScope.LocalUser
        );

        await Assert.That(trusted.IsSuccess).IsFalse();
        await Assert.That(confirmer.Warning).IsNull();
        await Assert.That(File.Exists(CreateSettingsStore(workspace).SettingsPath)).IsFalse();
    }

    [Test]
    public async Task TrustSources_WhenConfirmationDeclined_WritesNothing()
    {
        using var workspace = new TestWorkspace();
        Directory.CreateDirectory(Path.Combine(workspace.Path, "source"));
        var confirmer = new RejectingTrustConfirmer();
        var service = await CreateServiceAsync(workspace, confirmer);

        var trusted = await service.TrustSourcesAsync(
            workspace.Path,
            ["local"],
            TrustScope.LocalUser
        );

        await Assert.That(trusted.IsSuccess).IsFalse();
        await Assert.That(confirmer.Warning).Contains("filesystem and network access");
        await Assert.That(confirmer.Warning).Contains("irreversible external side effects");
        await Assert.That(confirmer.Warning).Contains("Scope: local user");
        await Assert.That(File.Exists(CreateSettingsStore(workspace).SettingsPath)).IsFalse();
    }

    [Test]
    public async Task ConsoleConfirmer_WhenConsoleNonInteractive_ReturnsFalseAfterWarning()
    {
        var output = new StringWriter();
        var ansiConsole = AnsiConsole.Create(
            new AnsiConsoleSettings
            {
                Ansi = AnsiSupport.No,
                ColorSystem = ColorSystemSupport.NoColors,
                Interactive = InteractionSupport.No,
                Out = new AnsiConsoleOutput(output),
            }
        );
        var confirmer = new ConsoleTrustConfirmer(new CliConsole(ansiConsole, CliLogLevel.Info));

        var confirmed = confirmer.Confirm("DANGER: exact trust scope");

        await Assert.That(confirmed).IsFalse();
        await Assert.That(output.ToString()).Contains("DANGER: exact trust scope");
    }

    [Test]
    public async Task Command_WhenNoScopeOption_DefaultsToLocalUser()
    {
        using var workspace = new TestWorkspace();
        Directory.CreateDirectory(Path.Combine(workspace.Path, "source"));
        var settingsStore = CreateSettingsStore(workspace);
        var application = new CliApplication(
            workspace.FileSystem,
            TestConsole.CreateAnsiConsole(),
            trustConfirmer: new AcceptingTrustConfirmer(),
            userSettingsStore: settingsStore
        );
        await application.RunAsync(["init"], workspace.Path);
        await application.RunAsync(["sources", "add", "local", "local", "source"], workspace.Path);

        var exitCode = await application.RunAsync(["trust", "source", "local"], workspace.Path);
        var settings = await settingsStore.LoadAsync();

        await Assert.That(exitCode).IsEqualTo(0);
        await Assert
            .That(settings.RequireValue().Projects.Values.Single().Sources)
            .Count()
            .IsEqualTo(1);
    }

    [Test]
    public async Task Command_WhenProjectAndGlobalCombined_FailsBeforeConfirmation()
    {
        using var workspace = new TestWorkspace();
        Directory.CreateDirectory(Path.Combine(workspace.Path, "source"));
        var confirmer = new AcceptingTrustConfirmer();
        var settingsStore = CreateSettingsStore(workspace);
        var application = new CliApplication(
            workspace.FileSystem,
            TestConsole.CreateAnsiConsole(),
            trustConfirmer: confirmer,
            userSettingsStore: settingsStore
        );
        await application.RunAsync(["init"], workspace.Path);
        await application.RunAsync(["sources", "add", "local", "local", "source"], workspace.Path);

        var exitCode = await application.RunAsync(
            ["trust", "source", "local", "--project", "--global"],
            workspace.Path
        );

        await Assert.That(exitCode).IsEqualTo(1);
        await Assert.That(confirmer.Warning).IsNull();
        await Assert.That(File.Exists(settingsStore.SettingsPath)).IsFalse();
    }

    [Test]
    public async Task Command_ListAndRevokeSource_AuditsThenRemovesLocalUserTrust()
    {
        using var workspace = new TestWorkspace();
        Directory.CreateDirectory(Path.Combine(workspace.Path, "source"));
        var output = new StringWriter();
        var ansiConsole = AnsiConsole.Create(
            new AnsiConsoleSettings
            {
                Ansi = AnsiSupport.No,
                ColorSystem = ColorSystemSupport.NoColors,
                Out = new AnsiConsoleOutput(output),
            }
        );
        var settingsStore = CreateSettingsStore(workspace);
        var application = new CliApplication(
            workspace.FileSystem,
            ansiConsole,
            trustConfirmer: new AcceptingTrustConfirmer(),
            userSettingsStore: settingsStore
        );
        await application.RunAsync(["init"], workspace.Path);
        await application.RunAsync(["sources", "add", "local", "local", "source"], workspace.Path);
        await application.RunAsync(["trust", "source", "local"], workspace.Path);

        var listExitCode = await application.RunAsync(["trust", "list"], workspace.Path);
        var revokeExitCode = await application.RunAsync(
            ["trust", "revoke", "source", "local"],
            workspace.Path
        );
        var settings = await settingsStore.LoadAsync();

        await Assert.That(listExitCode).IsEqualTo(0);
        await Assert
            .That(output.ToString().ReplaceLineEndings(string.Empty))
            .Contains("local-user source - identity: local");
        await Assert.That(revokeExitCode).IsEqualTo(0);
        await Assert.That(settings.RequireValue().Projects.Values.Single().Sources).IsEmpty();
    }

    [Test]
    public async Task ListAndRevokePacks_WhenGlobal_AuditsThenRemovesExactPack()
    {
        using var workspace = new TestWorkspace();
        Directory.CreateDirectory(Path.Combine(workspace.Path, "source"));
        var service = await CreateServiceAsync(workspace, new AcceptingTrustConfirmer());
        await service.TrustPacksAsync(workspace.Path, ["sdk"], "local", TrustScope.GlobalUser);

        var before = await service.ListAsync(workspace.Path, TrustScope.GlobalUser);
        var revoked = await service.RevokePacksAsync(
            workspace.Path,
            ["sdk"],
            "local",
            TrustScope.GlobalUser
        );
        var after = await service.ListAsync(workspace.Path, TrustScope.GlobalUser);

        await Assert.That(before.RequireValue().Packs.Single().Id).IsEqualTo("sdk");
        await Assert.That(revoked.IsSuccess).IsTrue();
        await Assert.That(after.RequireValue().Packs).IsEmpty();
    }

    [Test]
    public async Task RevokeSources_WhenProject_RemovesDeclarationAndAcknowledgement()
    {
        using var workspace = new TestWorkspace();
        Directory.CreateDirectory(Path.Combine(workspace.Path, "source"));
        var service = await CreateServiceAsync(workspace, new AcceptingTrustConfirmer());
        await service.TrustSourcesAsync(workspace.Path, ["local"], TrustScope.Project);

        var revoked = await service.RevokeSourcesAsync(
            workspace.Path,
            ["local"],
            TrustScope.Project
        );
        var listing = await service.ListAsync(workspace.Path, TrustScope.Project);

        await Assert.That(revoked.IsSuccess).IsTrue();
        await Assert.That(listing.RequireValue().ProjectSourceDeclarations).IsEmpty();
        await Assert.That(listing.RequireValue().ProjectSourceAcknowledgements).IsEmpty();
    }

    [Test]
    public async Task RevokeSources_WhenAnySourceUnknown_PreservesExistingTrust()
    {
        using var workspace = new TestWorkspace();
        Directory.CreateDirectory(Path.Combine(workspace.Path, "source"));
        var service = await CreateServiceAsync(workspace, new AcceptingTrustConfirmer());
        await service.TrustSourcesAsync(workspace.Path, ["local"], TrustScope.LocalUser);

        var revoked = await service.RevokeSourcesAsync(
            workspace.Path,
            ["local", "missing"],
            TrustScope.LocalUser
        );
        var listing = await service.ListAsync(workspace.Path, TrustScope.LocalUser);

        await Assert.That(revoked.IsSuccess).IsFalse();
        await Assert.That(listing.RequireValue().Sources).Count().IsEqualTo(1);
    }

    [Test]
    public async Task TrustSources_WhenProjectSettingsSaveFails_RollsBackProjectDeclaration()
    {
        using var workspace = new TestWorkspace();
        Directory.CreateDirectory(Path.Combine(workspace.Path, "source"));
        var settingsStore = CreateSettingsStore(workspace);
        var confirmer = new CallbackTrustConfirmer(() =>
            Directory.CreateDirectory(settingsStore.SettingsPath)
        );
        var service = await CreateServiceAsync(workspace, confirmer);

        var trusted = await service.TrustSourcesAsync(
            workspace.Path,
            ["local"],
            TrustScope.Project
        );
        var state = await workspace.StateStore.LoadAsync(workspace.Path);

        await Assert.That(trusted.IsSuccess).IsFalse();
        await Assert.That(state.RequireValue().Configuration.Trust.Sources).IsEmpty();
    }

    private static async Task<TrustService> CreateServiceAsync(
        TestWorkspace workspace,
        ITrustConfirmer confirmer
    )
    {
        var state = new ProjectState
        {
            Configuration = new ProjectConfiguration
            {
                SchemaVersion = 1,
                Sources =
                [
                    new ProjectConfiguration.LocalSource { Name = "local", Path = "source" },
                ],
            },
            LockFile = new ProjectLockFile { SchemaVersion = 1 },
        };
        await Assert
            .That((await workspace.StateStore.SaveAsync(workspace.Path, state)).IsSuccess)
            .IsTrue();
        return new TrustService(
            workspace.FileSystem,
            workspace.StateStore,
            CreateSettingsStore(workspace),
            confirmer
        );
    }

    private static UserSettingsStore CreateSettingsStore(TestWorkspace workspace) =>
        new(workspace.FileSystem, Path.Combine(workspace.Path, "user-profile"));

    private sealed class AcceptingTrustConfirmer : ITrustConfirmer
    {
        public string? Warning { get; private set; }

        public bool Confirm(string warning)
        {
            Warning = warning;
            return true;
        }
    }

    private sealed class RejectingTrustConfirmer : ITrustConfirmer
    {
        public string? Warning { get; private set; }

        public bool Confirm(string warning)
        {
            Warning = warning;
            return false;
        }
    }

    private sealed class CallbackTrustConfirmer(Action callback) : ITrustConfirmer
    {
        public bool Confirm(string warning)
        {
            callback();
            return true;
        }
    }
}
