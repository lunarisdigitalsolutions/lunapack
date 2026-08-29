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
