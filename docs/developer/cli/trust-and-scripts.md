# Lifecycle hooks and script trust

Pack lifecycle scripts are optional executable hooks. They are not sandboxed:
an allowed hook runs with the invoking user's filesystem, process, network, and
credential access. Review the pack source and hook arguments before permitting
execution. Instruction hooks display prepared Markdown but never launch a
process and do not use trust. See the [security model](../threat-model.md) for
trust boundaries and residual risks.

## Script modes

`luna install`, `luna update`, and `luna uninstall` accept `--scripts`:

| Mode     | Behavior                                                         |
| -------- | ---------------------------------------------------------------- |
| `prompt` | Default. Uses persisted trust or asks; Enter declines execution. |
| `run`    | Allows all non-suppressed hooks for this invocation only.        |
| `skip`   | Runs no script hooks.                                            |

Persisted script denial overrides all three modes and every source or pack
grant. It is evaluated before command resolution or confirmation.

Dry runs do not execute hooks or prompt. They show hook order, source, command or
runner, arguments, suppression, and expected consent mode. Policy-denied rows
show every denying scope. Instruction rows show
their file, effective templating state, and step count.

Use `--skip-instructions` to prevent instruction loading and display without
changing `--scripts`. Interactive instructions pause after each H2/H3 step;
noninteractive instructions print completely and never read input.
Instruction display does not require consent because it cannot execute code.

## Hook input and output

In an interactive terminal, approved hooks inherit LunaPack's standard input,
output, and error streams. Commands such as `npm init` can display their own
prompts and read answers directly. Their output is not prefixed or reformatted
by LunaPack.

In noninteractive sessions, LunaPack captures bounded hook output. Hooks used in
automation must not require input; configure the underlying command's
noninteractive or accept-defaults option instead.

## Trust scopes

Trust always identifies a configured source. Pack-level trust also identifies a
bare pack ID.

```powershell
luna trust source engineering
luna trust pack dotnet-project --source engineering
luna trust list
```

Omitting a scope stores trust in local-user settings for the current project.
Use `--project` to persist portable trust in `lunapack.yml`, or `--global` to
apply current-user trust across projects. These options are mutually exclusive.
Project trust should be committed only when every consumer is expected to trust
that source.

Revoke entries with matching identity and scope:

```powershell
luna trust revoke source engineering
luna trust revoke pack dotnet-project --source engineering
luna trust list --global
```

Set blanket denial with the same scope rules:

```powershell
luna trust scripts deny
luna trust scripts deny --project
luna trust scripts deny --global
luna trust list --project
```

Omitting a scope targets project-local user settings. Project denial is stored
under `trust.deny.scripts` in `lunapack.yml` and needs no acknowledgement.
Local-user and global-user denial are stored under `deny.scripts` in user
settings. Multiple denials compose; all applicable scopes must be reset before
scripts can run.

```powershell
luna trust scripts reset
luna trust scripts reset --project
luna trust scripts reset --global
```

Reset warns and requires interactive confirmation because denial preserves
positive grants and the last reset can reactivate them. Noninteractive reset
fails without changing settings. During lifecycle work, Luna warns once per
denied hook with pack, version, event, and all policy origins before processing
instructions or managed files. The operation then continues without scripts.

Project configuration example:

```yaml
trust:
  deny:
    scripts: true
```

Omission and explicit `false` both mean no denial. Existing version-1 files and
explicit empty trust collections remain valid.

Trust does not verify publisher identity, sign pack content, pin a registry, or
restrict what a hook can do. Git lock provenance records a resolved commit for
reproducibility, but it is not a signature.

## Lifecycle order

Supported events are `preInstall`, `postInstall`, `preUpdate`, `postUpdate`,
`preUninstall`, and `postUninstall`. Luna processes dependencies before their
consumers and preserves declaration order within each event. New dependencies
introduced by an update use install hooks.

Uninstall retrieves hooks from the exact installed releases. If source content
is unavailable, Luna warns, skips those hooks, and continues removal rather
than substituting hooks from another release.

## Author requirements

Authors should prefer declarative managed files or non-executable instructions
over scripts. When a script is necessary, use a direct executable plus discrete
argv values, describe its effects, keep packed script paths within the pack, and
avoid reading credentials unless the capability requires them. Arguments
support Scriban expressions over resolved pack parameters. LunaPack renders
them before dry-run output, consent, trust authorization, and execution, so
approval binds to exact argv. Composite packs can suppress selected lifecycle
events with `disabledHooks`; suppression applies to both hook types.

See [Use Scriban templates](../packs/how-to/use-scriban-templates.md) for syntax,
examples, and restrictions.
