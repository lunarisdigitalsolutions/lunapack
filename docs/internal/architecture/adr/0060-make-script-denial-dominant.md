---
status: accepted
date: 2026-08-28
decision-makers: LunaPack maintainers
---

# ADR-0057: Make Script Denial Dominant

## Context and Problem Statement

LunaPack supports portable project trust, project-local user trust, global-user
trust, and invocation-level script modes. Positive grants and `--scripts run`
could authorize executable lifecycle hooks, but consumers had no persistent
policy that categorically prevented execution. A denial policy must compose
across scopes without allowing a narrower setting or command option to weaken a
broader restriction.

## Decision Drivers

- Let projects and users persist a blanket script restriction.
- Make every applicable denial impossible to bypass with grants or script mode.
- Explain denied hooks before lifecycle work starts.
- Preserve independently managed grants and non-executable instructions.
- Keep version-1 configuration backward compatible.

## Considered Options

- Treat any applicable script denial as dominant.
- Resolve denial by scope precedence.
- Map persisted denial to the invocation-level skip mode.
- Delete positive grants when denial is enabled.

## Decision Outcome

Chosen option: "Treat any applicable script denial as dominant", because
restrictions must compose monotonically across portable project, project-local
user, and global-user policy.

`deny.scripts: true` is evaluated in project, local-user, global-user order
before script mode, positive trust, executable resolution, or confirmation. Any
active denial prevents every applicable script from being authorized. LunaPack
reports all active origins in that stable order.

Denial does not remove source or pack grants. Resetting denial can therefore
reactivate retained grants and requires interactive confirmation. Portable
project denial requires no local acknowledgement because it reduces authority.

LunaPack warns once per denied hook, including pack, version, lifecycle event,
and every denying scope, before processing any hook or managed-file mutation.
Denied scripts are omitted; instructions and managed-file work continue. Dry
runs report `policy-denied` and origins without emitting execution warnings.

### Consequences

- Good, because no invocation option or positive grant can bypass policy.
- Good, because multiple scopes strengthen rather than override one another.
- Good, because temporary denial does not destroy separately managed grants.
- Good, because lifecycle state can still converge while scripts are disabled.
- Bad, because resetting the last denial can reactivate old grants.
- Bad, because packs that require scripts may complete without their scripted
  setup; warnings make that omission explicit.

### Confirmation

Schema, trust-service, authorization, lifecycle, dry-run, and real CLI process
tests verify all scopes, ordered origins, retained grants, confirmation,
non-bypass, warning timing, instruction retention, and continued state changes.

## Pros and Cons of the Options

### Dominant denial

- Good, because policy composition cannot reduce restrictions.
- Bad, because every active scope must be cleared before scripts can run again.

### Scope precedence

- Good, because one scope could intentionally override another.
- Bad, because a narrower or command-local choice could weaken a security policy.

### Invocation skip mode

- Good, because existing skip behavior could be reused directly.
- Bad, because it loses persisted scope attribution and bypass ordering.

### Delete positive grants

- Good, because reset could not reactivate prior authority.
- Bad, because a temporary restriction would destroy independent trust choices.

## More Information

See [ADR-0040](0040-secure-lifecycle-scripts-with-scoped-trust.md),
[ADR-0055](0055-extend-recoverable-lifecycle-hooks-through-uninstall.md), and
[Lifecycle Hook Safety](../../development/lifecycle-script-safety.md).
