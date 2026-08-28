# Use lifecycle hooks

Packs can run scripts or show instructions before and after installation,
updates, and uninstallation. Review hooks before allowing them because scripts
execute with your user account's authority.

## Choose script behavior

Select script behavior for each install, update, or uninstall:

```powershell
luna install dotnet-project --scripts prompt
luna install dotnet-project --scripts run
luna install dotnet-project --scripts skip
luna uninstall dotnet-project --scripts prompt
```

`prompt` is the default. It uses existing source or source-plus-pack trust and
otherwise asks before each untrusted script. Pressing Enter declines execution;
users must select yes explicitly. `run` permits all non-suppressed
scripts for this invocation. `skip` runs no scripts.

Persistent denial overrides every mode and grant. Use `luna trust scripts deny`
with optional `--project` or `--global`; use the corresponding `reset` command
only when retained grants may become active again. Luna warns for each denied
hook before processing lifecycle work, then continues with instructions and
managed files.

Grant persistent trust only after reviewing a source or pack:

```powershell
luna trust source engineering
luna trust pack dotnet-project --source engineering
```

See [Lifecycle hooks and script trust](cli/trust-and-scripts.md) for trust
scope, revocation, execution order, and noninteractive behavior.

## Follow instructions

Instruction hooks display publisher-provided Markdown and never launch a
process. Interactive terminals show one prepared step at a time; noninteractive
sessions print all prepared content without reading input.
Instructions display automatically unless skipped. Luna omits the document H1,
emphasizes step headings, and formats bold, italic, code, and links when the
terminal supports ANSI styling.

Use `--skip-instructions` to prevent instruction loading and display without
changing script behavior. Install, update, and uninstall accept the same hook
controls:

```powershell
luna install dotnet-project --skip-instructions
```

Dry runs validate and summarize scripts and instructions without prompting,
starting a process, or entering guided display. Denied script rows show
`policy-denied` and every applicable scope without execution warnings.

## Understand hook timing

Supported events are `preInstall`, `postInstall`, `preUpdate`, `postUpdate`,
`preUninstall`, and `postUninstall`. Luna processes dependencies before their
consumers and preserves declaration order within each event. New dependencies
introduced by an update use install hooks.

Uninstall hooks come from the exact installed releases. Luna warns and
continues without hooks when those releases cannot be retrieved, so a deleted
or unreachable source cannot prevent removal.

Pack authors can define and template hooks in `pack.yml`. See the
[pack manifest reference](packs/reference/manifest.md#lifecycle-hooks) and
[Scriban guide](packs/how-to/use-scriban-templates.md#render-lifecycle-hooks).
