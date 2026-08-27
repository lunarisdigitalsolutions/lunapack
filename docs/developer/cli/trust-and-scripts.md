# Lifecycle hooks and script trust

Pack lifecycle scripts are optional executable hooks. They are not sandboxed:
an allowed hook runs with the invoking user's filesystem, process, network, and
credential access. Review the pack source and hook arguments before permitting
execution. Instruction hooks display prepared Markdown but never launch a
process and do not use trust.

## Script modes

`luna install`, `luna update`, and `luna uninstall` accept `--scripts`:

| Mode     | Behavior                                                         |
| -------- | ---------------------------------------------------------------- |
| `prompt` | Default. Uses persisted trust or asks; Enter declines execution. |
| `run`    | Allows all non-suppressed hooks for this invocation only.        |
| `skip`   | Runs no script hooks.                                            |

Dry runs do not execute hooks or prompt. They show hook order, source, command or
runner, arguments, suppression, and expected consent mode. Instruction rows show
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

Trust does not verify publisher identity, sign pack content, pin a registry, or
restrict what a hook can do. Git lock provenance records a resolved commit for
reproducibility, but it is not a signature.

## Author requirements

Authors should prefer declarative managed files or non-executable instructions
over scripts. When a script is necessary, use a direct executable plus discrete
argv values, describe its effects, keep packed script paths within the pack, and
avoid reading credentials unless the capability requires them. Arguments
support Scriban expressions over resolved pack parameters. LunaPack renders
them before dry-run output, consent, trust authorization, and execution, so
approval binds to exact argv. Composite packs can suppress selected lifecycle
events with `disabledHooks`; suppression applies to both hook types.

Uninstall retrieves hooks from the exact installed release. If that source
content is unavailable, LunaPack warns, skips those hooks, and continues the
removal. It never substitutes hooks from a different release.

See [Use Scriban templates](../packs/how-to/use-scriban-templates.md) for syntax,
examples, and restrictions.
