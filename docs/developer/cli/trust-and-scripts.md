# Lifecycle hooks and script trust

Pack lifecycle scripts are optional executable hooks. They are not sandboxed:
an allowed hook runs with the invoking user's filesystem, process, network, and
credential access, including the full inherited process environment. Luna warns
about unlisted environment variable names and applies a per-hook timeout, but
neither limits what an authorized script can access. Review the pack source and
hook arguments before permitting execution. Instruction hooks display prepared
Markdown but never launch a process and do not use trust. See the
[security model](../threat-model.md) for trust boundaries and residual risks.

## Script modes

`luna install`, `luna update`, and `luna uninstall` accept `--scripts`:

| Mode     | Behavior                                                         |
| -------- | ---------------------------------------------------------------- |
| `prompt` | Default. Uses persisted trust or asks; Enter declines execution. |
| `run`    | Allows all non-suppressed hooks for this invocation only.        |
| `skip`   | Runs no script hooks.                                            |

Persisted script denial overrides all three modes and every source or pack
grant. It is evaluated before command resolution or confirmation.

Before requesting consent, LunaPack displays the pack ID, resolved command and
arguments, and optional script description, regardless of the minimum log level.

Dry runs do not execute hooks or request hook consent. They may prompt for pack
parameters before planning unless `--skip-parameters` is set. They show each
hook phase, pack and version, suppression, and expected consent source. Script rows report applicable
policy-denial scopes, an explicit `--scripts run|skip` override, matching
project/local-user/global-user trust scopes, or the need for confirmation.
Instruction rows show their file, effective templating state, and step count.

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

## Process termination

Timeout and cancellation stop the hook process tree. Windows uses an isolated
job object. Linux uses a child-subreaper process and process-group containment.
macOS tracks hook session and process relationships and terminates descendants
through process groups and descendant process IDs.

A hook is not considered complete until its root process and active descendants
have exited or been terminated. A script that exits but leaves a detached
child continues to run the hook until the child stops or the timeout/cancellation
policy ends the tree.

When cancellation stops a hook's process tree, LunaPack closes redirected
readers instead of waiting for detached descendants that retain output handles.
Output already captured is emitted; unread output may be discarded.

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

Project trust has two parts: the portable declaration in `lunapack.yml` and an
acknowledgement bound to the current user, canonical project path, and exact
source identity. Both must match before the declaration authorizes a hook.
`luna trust ... --project` writes both parts for the invoking user. Committing
the declaration does not grant trust to another user, another checkout path, or
a fresh `LUNAPACK_USER_PROFILE`; each must review it and run the corresponding
`--project` trust command, choose another trust scope, use `--scripts run` for a
reviewed invocation, or skip scripts.

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

## Lifecycle script policy

Script policy controls environment warnings and execution time only. It does
not authorize a script or override script denial.

Portable policy belongs in `lunapack.yml`:

```yaml
trust:
  scripts:
    allowedEnvironmentVariables:
      - CUSTOM_TOOL_HOME
    timeoutSeconds: 120
```

`allowedEnvironmentVariables` adds names to a built-in platform allow-list.
Names from project, project-local user, and global-user policy are combined.
Windows compares names without regard to case; other platforms compare names
ordinally. Empty names, duplicate names, `=`, and NUL are invalid. The built-in
list includes `PATH`, `TEMP`, `TMP`, and `TMPDIR` on every platform. Windows
also includes `PATHEXT`, `SYSTEMROOT`, `WINDIR`, `COMSPEC`, `USERPROFILE`,
`HOMEDRIVE`, `HOMEPATH`, `APPDATA`, `LOCALAPPDATA`, and `SYSTEMDRIVE`; other
platforms also include `HOME`, `SHELL`, `LANG`, `LC_ALL`, `LC_CTYPE`, and
`TERM`.

Luna reports names outside the effective list before an authorized script
starts. It never includes their values in the warning. The script still
receives every inherited variable; adding a name only suppresses its warning.
This policy is not environment filtering or isolation.

Global-user and project-local user policy live in
`<profile>/.lunapack/lifecycle-hook-policy.yml`, separate from `config.yml`:

```yaml
global:
  allowedEnvironmentVariables:
    - CUSTOM_TOOL_HOME
  timeoutSeconds: 180
projects:
  /workspace/my-project:
    timeoutSeconds: 60
```

Edit the sidecar directly; Luna has no policy-editing command.
Replace the `projects` key with the project's canonical absolute path using
`/` separators and the filesystem's final physical spelling; for example, use
`'C:/work/my-project'` on Windows. Luna normalizes existing differently cased
or aliased keys when loading the sidecar. The `LUNAPACK_USER_PROFILE` variable
selects the profile root.

`timeoutSeconds` accepts integers from 1 through 1,800. Each script hook uses
the smallest configured value across project, project-local user, and
global-user policy, or 300 seconds when no scope sets a value. The deadline
covers root-process exit, remaining descendants, and captured-output draining.
A root exit with active descendants does not complete the hook. On timeout,
Luna terminates the process tree and fails the lifecycle operation; caller
cancellation is reported separately. A failed post-hook rolls back Luna
managed files and project state, but cannot undo external effects caused by
the script.

Existing version-1 project files may omit `trust.scripts`. Older Luna versions
ignore this project property and the user sidecar, so they do not issue these
warnings or enforce timeouts. Older strict external schema validators may reject
the new optional project property.

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
