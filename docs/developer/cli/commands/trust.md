# `luna trust`

Manage lifecycle script authorization and blanket script denial.

## Syntax

```text
luna trust source <name>... [scope]
luna trust pack <id>... --source <name> [scope]
luna trust scripts deny|reset [scope]
luna trust list [scope]
luna trust revoke source <name>... [scope]
luna trust revoke pack <id>... --source <name> [scope]
```

## Scope

| Option      | Scope                                                                          |
| ----------- | ------------------------------------------------------------------------------ |
| `--project` | Portable project declaration plus the invoking user's project acknowledgement. |
| `--global`  | Global user trust across projects.                                             |
| No scope    | Local-user trust for the canonical current project.                            |

Scope options are mutually exclusive. Project script denial does not need a
source or pack identity.

## Trust behavior

Source trust identifies the configured source identity. Pack trust identifies
both a bare pack ID and source. Explicit persisted denial overrides every grant
and `--scripts run`.

Project trust uses both `lunapack.yml` and the current user's project
acknowledgement. Committing project trust alone does not authorize scripts for
another user or checkout.

## Script denial

```powershell
luna trust scripts deny
luna trust scripts deny --project
luna trust scripts deny --global
```

Reset denial with `luna trust scripts reset` and its matching scope. Interactive
reset is required; noninteractive reset does not change state.

## Exit behavior

Successful trust mutations return `0`. Conflicting scope, missing source
identity, active consumers, or invalid reset state returns a nonzero result
and leaves state unchanged.
