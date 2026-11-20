# Audit ownership and recover drift

Use `luna audit` to inspect resolved ownership, external-source provenance, and
the current state of installed Luna Links before changing project state by
hand.

## Inspect recorded state

Run the audit from the project workspace:

```powershell
luna audit
```

The resolved-packs table identifies each exact release, source path, direct
dependencies, and managed targets. External pack content adds source and file
records with fingerprints, refs, commits, digests, and status. Installed Luna
Links include each target's status.

Audit also screens existing valid UTF-8 managed targets. A finding includes the
installed project-relative path, line, Unicode-scalar column, code point, and
severity. Bidi controls and U+200B are high-confidence findings; other Unicode
format characters and orphan variation selectors are warnings. Opaque
NUL-containing or invalid UTF-8 content can still expose recoverable controls.
Audit remains read-only, so these findings do not change its exit status and
never modify files, configuration, or lock state.

## Automate the audit

Use JSON when a script or CI job must inspect the result:

```powershell
luna audit --format json
```

JSON goes to standard output; diagnostics go to standard error. The result
contains `packs`, `links`, `externalSources`, and `externalFiles` arrays.
Top-level `findings` provide stable identifiers, and `summary` provides
`totalFindings` plus `info`, `warning`, and `error` counts. Audit does not change
its exit behavior because a finding has a particular severity. See the
[command reference](../cli/commands.md#machine-readable-command-output) for
the complete envelope and compatibility policy.

JSON status values are stable tokens:

| Result          | JSON status values                                                                       |
| --------------- | ---------------------------------------------------------------------------------------- |
| External source | `current`, `missing-workspace-source`, `invalid-workspace-source`, `configuration-drift` |
| External file   | `current`, `missing-target`, `modified-target`                                           |
| Link file       | `current`, `missing-target`, `modified-target`, `conflicting-owner`                      |

Each finding includes `code`, `severity`, `subject`, and a human-readable
`message`. External-source and external-file subjects use the source alias as
`id` and include the owning `packId`. External-file and link-file subjects
include the project-relative `path`; link-file subjects use the link name as
`id`. Hidden-Unicode findings use the managed-file path as `id` and also
include stable `line`, `column`, and `codePoint` fields. Message wording is not
a stable contract.

Audit findings use these schema-version-1 identifiers:

| Code                                        | Severity  |
| ------------------------------------------- | --------- |
| `audit.external-source.missing-workspace`   | `warning` |
| `audit.external-source.invalid-workspace`   | `error`   |
| `audit.external-source.configuration-drift` | `warning` |
| `audit.external-file.missing-target`        | `warning` |
| `audit.external-file.modified-target`       | `warning` |
| `audit.hidden-unicode.high-confidence`      | `error`   |
| `audit.hidden-unicode.warning`              | `warning` |
| `audit.hidden-unicode.omitted`              | `warning` |
| `audit.link-file.missing-target`            | `warning` |
| `audit.link-file.modified-target`           | `warning` |
| `audit.link-file.conflicting-owner`         | `error`   |

## Interpret statuses

External source records use these statuses:

| Status                     | Meaning                                                    |
| -------------------------- | ---------------------------------------------------------- |
| `current`                  | Configured source identity matches locked provenance.      |
| `missing workspace source` | Locked source name is absent from `lunapack.yml`.          |
| `invalid workspace source` | Current source configuration cannot form a valid identity. |
| `configuration drift`      | Current source identity differs from the locked identity.  |

External managed files use `current`, `missing target`, or `locally modified`.
Luna Link targets use `ok`, `missing`, `modified`, or `conflicting`.

The resolved-packs table does not assign a current/modified status to ordinary
pack files. Their paths and recorded ownership remain visible; lifecycle
commands apply each file's declared strategy when planning changes.

## Gate CI on managed-file integrity

Run strict mode when automation must fail for missing or modified managed
content:

```powershell
luna audit --strict
```

Strict audit uses text output. `--format json` applies to report-only audit;
combining it with `--strict` returns exit code `1` and a JSON failure envelope.

Strict mode reads every pack and Luna Link target recorded in
`lunapack-lock.yml`, computes its SHA-256 digest, and reports the owner, path,
and `current`, `missing`, or `modified` status. Before opening a target, Luna
normalizes it beneath the selected workspace and rejects symbolic links or
reparse-point ancestors and targets. It does not follow audit paths outside the
workspace, resolve a current source, or change project files or lock state.

Add `--fail-fast` to stop after the first missing or modified target:

```powershell
luna audit --strict --fail-fast
```

`--fail-fast` requires `--strict`. Without `--strict`, audit keeps its existing
report-only behavior and successful exit status for managed-content findings.

Strict audit exit codes distinguish content violations from command failures:

| Exit code | Meaning                                                     |
| --------- | ----------------------------------------------------------- |
| `0`       | Every recorded target could be read and matches its digest. |
| `1`       | Command usage or project or target state could not be read. |
| `2`       | One or more recorded targets were missing or had changed.   |

Source-provenance drift and ownership conflicts do not by themselves change the
strict integrity outcome. They remain informational audit statuses. Unsafe
paths, malformed collection state, inaccessible targets, and filesystem aliases
are operation errors and return `1`.

## Recover deliberately

- For configuration drift, compare the source URL, ref, and base path with the
  locked fingerprint. Restore the intended source configuration or reinstall
  from an explicitly reviewed source. Do not edit fingerprints by hand.
- For a missing target, decide whether to restore it through `luna update` or
  remove its owning root with `luna uninstall`.
- For modified content, review the local diff before update or uninstall.
  Update behavior depends on the managed-file strategy.
- For conflicting Luna Link ownership, inspect `luna links show <name>` and the
  other reported owner before changing either definition.

Preview `luna update --dry-run` after reconciliation. Keep
`lunapack-lock.yml` under version control when the project commits generated
ownership state, and review unexpected lock changes rather than manufacturing
new digests.

See the [lock file reference](../cli/lock-file.md) for every recorded field and
[Troubleshooting](../troubleshooting.md) for common failure paths.
