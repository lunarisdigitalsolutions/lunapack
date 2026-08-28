# Deny Lifecycle Scripts Design

## Context

See [proposal.md](proposal.md) for motivation. LunaPack currently stores positive source and pack grants in portable project configuration, global-user settings, and project-local user settings. Project grants require a user acknowledgement because they add execution authority. `LifecycleHookAuthorizer` applies invocation-level script mode before loading trust, so `--scripts run` currently bypasses persisted trust evaluation. Hook planning already gathers pre- and post-mutation hooks before any hook or managed-file action.

Project, lock, pack, and user-settings serializers materialize in-memory defaults such as empty optional collections. The schemas accept several of those properties as optional, but initialization passes default-filled models through the normal persistence path and therefore emits them.

The delta specifications define the observable behavior. This design preserves the catalog, provider, dependency-resolution, lifecycle-planning, and process-execution boundaries. Script denial remains consumer policy and never enters pack manifests or external-content consent.

## Goals / Non-Goals

**Goals:**

- Represent one default-off script-denial policy consistently across portable project, global-user, and project-local user scopes.
- Make denial impossible to bypass with an invocation option or positive trust grant.
- Explain every denial before lifecycle processing while preserving instructions and managed-file work.
- Keep version-1 project and user-settings documents backward compatible.
- Produce minimal schema-valid project, lock, and pack YAML during initialization without changing unrelated persistence formatting.

**Non-Goals:**

- Add per-source, per-pack, per-hook, or version-specific denial entries.
- Delete or rewrite positive grants when denial is enabled.
- Treat instruction hooks as scripts or suppress them through script denial.
- Sandbox lifecycle processes or change process execution and rollback semantics.
- Apply the initialization-only minimal-output rule to every later configuration rewrite.

## Decisions

### 1. Model denial separately from positive trust acknowledgements

Add a nested denial value with a `Scripts` boolean to each effective trust scope:

```yaml
trust:
  deny:
    scripts: true
```

Portable project trust owns the first shape. Global-user trust and project-local user trust use the equivalent `deny.scripts` shape beneath their existing records in `~/.lunapack/config.yml`. Omission and explicit `false` both mean no denial; generated settings omit the default false value.

Keep project acknowledgements as positive source and pack identities only. Do not reuse a denial-bearing scope model for acknowledgements. Portable denial applies directly without acknowledgement because accepting a copied project policy cannot add authority.

Alternatives considered:

- Store denial as a negative source or pack identity. Rejected because the requested policy is graph-wide and source-independent.
- Store a `scripts: prompt|run|skip` default. Rejected because persisted `run` would add a second broad grant path and weaken the trust model.
- Require user acknowledgement for project denial. Rejected because acknowledgement protects users from portable authority grants, not restrictions.

### 2. Evaluate denial as a dominant policy before authorization mode

Introduce a policy result that reports whether scripts are denied and the complete ordered set of denying scopes: `project`, `local-user`, then `global-user`. Evaluate it for every non-dry-run lifecycle plan before `ScriptExecutionMode`, source trust, pack trust, command resolution, or confirmation. Any true value denies all applicable scripts. This ordering makes `--scripts run`, `prompt`, and `skip` incapable of changing the outcome and ensures warnings consistently attribute policy rather than an invocation option.

When denial is absent, retain the current `prompt`, `run`, and `skip` paths. Positive grants remain stored and become effective again only after every applicable denial is cleared. A settings-load or validation failure continues to fail closed whenever user settings are needed to establish the effective policy.

The authorizer will return authorized hooks plus policy-denial diagnostics rather than silently discarding denied scripts. It will not resolve executables for denied scripts, matching the existing safety and performance behavior of explicit skipping.

Alternatives considered:

- Let `--scripts run` override persisted denial. Rejected because a command parameter would defeat the policy's purpose.
- Give local scope precedence over global scope. Rejected because precedence permits a narrower setting to weaken a broader restriction; denial must compose monotonically.
- Convert denial into `ScriptExecutionMode.Skip`. Rejected because that loses scope attribution and cannot distinguish policy from explicit invocation choice.

### 3. Warn for every denied hook before lifecycle processing

Keep graph-wide authorization before hook dispatch. After policy evaluation, emit one warning per denied script in planned order. Each warning identifies pack ID and version, lifecycle event, and all denying scopes. Emit the complete warning set before any instruction, script, or managed-file mutation. Then remove denied scripts from the authorized hook sequence while retaining instruction entries and their original relative order.

Dry runs load and evaluate the policy but do not emit execution warnings because no hook would execute. Their hook rows report `policy-denied` and the same ordered scopes. A plan without scripts emits no denial warning.

Alternatives considered:

- Emit one aggregate warning. Rejected because users need to identify every skipped pack hook, including transient dependencies and post hooks.
- Warn during dispatch. Rejected because post-hook warnings could appear only after managed files mutate.
- Resolve and print executable commands. Rejected because denial occurs before command resolution and pack plus lifecycle event provide sufficient attribution.

### 4. Extend trust commands with deny and reset operations

Add `luna trust scripts deny` and `luna trust scripts reset`, reusing existing mutually exclusive `--project` and `--global` selection with project-local user scope as the default. Deny mutations reduce authority and therefore require no confirmation. Reset can reactivate retained grants, so it uses a dedicated warning confirmation and fails closed when interaction is unavailable.

Project denial writes only `lunapack.yml`; it does not create an acknowledgement. Local and global denial writes use the existing atomic user-settings store. Trust listing adds a stable script-denial row for the selected scope and continues listing retained grants. Reset removes only the selected denial and leaves all grants and other scopes unchanged.

Alternatives considered:

- Reuse `trust revoke`. Rejected because revocation currently removes positive source or pack grants and does not communicate that reset may reactivate them.
- Clear grants when enabling denial. Rejected because a temporary policy change should not destroy independently managed trust decisions.

### 5. Keep schema version 1 and make trust collections optional

Extend project and user-settings schemas with optional `deny.scripts`. Make source and pack grant collections optional within trust records so a denial-only record is valid and minimal. Runtime models still normalize omitted collections and policy values to empty grants and false denial. Validation rejects denial under project acknowledgement records.

No eager migration is required. Existing documents retain their meaning, and explicit empty collections remain valid. Successful writes may omit a false denial but must not change trust scope or identity semantics.

Alternatives considered:

- Increment project schema version. Rejected because all new properties are optional and omission preserves existing behavior.
- Require empty grant arrays beside denial. Rejected because they add the configuration noise this change is also reducing.

### 6. Use initialization-specific minimal serialization projections

Keep normal project, lock, and pack persistence behavior stable. Add structured initialization projections containing only each schema's required properties and required values collected by the command. Validate the projected YAML through the existing model and schema validation path before atomically creating files.

`luna init` writes project `schemaVersion`, `sources`, and `packs`, plus lock `schemaVersion` and `packs`. `luna pack init` writes required `author`, `id`, `license`, and `version`; required values remain present when they came from defaults. Optional empty collections and default-valued optional properties are absent. Later commands can persist properties needed by their operations through existing stores.

Alternatives considered:

- Enable global empty-collection omission on every serializer. Rejected because required empty project and lock collections would disappear and unrelated saves could create broad formatting churn.
- Hand-build YAML text. Rejected because it bypasses structured serialization and risks contract drift.
- Make every optional in-memory property nullable. Rejected because it spreads persistence concerns through lifecycle code that benefits from non-null collections.

### 7. Record the durable policy decision and update audience-specific guidance

Create a new accepted ADR for dominant script denial, scope composition, and retained grants. It complements the existing scoped-trust ADR rather than rewriting it. Update product requirements for the new user control, internal runtime and lifecycle-script safety guidance for evaluation order, and developer reference/how-to pages for configuration, commands, warnings, dry runs, and reset risk. Do not document the behavior as available until implementation lands.

## Risks / Trade-offs

- **Retained grants become active after reset** → Require an explicit warning confirmation and list grants independently from denial.
- **Multiple active denials confuse remediation** → Report every denying scope in deterministic order for warnings, dry runs, and trust listings.
- **A policy check changes `--scripts skip` behavior when settings are invalid** → Treat policy resolution as a security boundary and fail closed rather than allowing an invocation option to bypass unreadable settings.
- **Initialization output can diverge from later canonical writes** → Keep projections schema-backed and test load-save compatibility after initialization.
- **Portable denial can disable setup required by a pack** → Continue lifecycle by explicit product decision, warn for each skipped hook, and preserve instruction guidance.
- **Concurrent external-source work also touches lifecycle planning** → Keep denial evaluation after graph and hook planning but before executable authorization, independent from external-content approval and source-fingerprint implementation.

## Migration Plan

1. Extend schemas and runtime models while retaining version `1` and omission defaults.
2. Add trust command persistence and listing behavior before lifecycle enforcement so policy state is inspectable.
3. Add dominant policy evaluation, diagnostics, dry-run reporting, and lifecycle tests across install, update, and uninstall.
4. Introduce minimal initialization projections and verify generated files against published schemas and normal stores.
5. Add the ADR and update product, internal, and developer documentation in the same change.

Rollback removes command and enforcement behavior while leaving optional denial properties readable and ignored only in an unreleased build. After release, rollback must retain denial parsing and fail closed; silently ignoring a persisted security restriction is not acceptable.
