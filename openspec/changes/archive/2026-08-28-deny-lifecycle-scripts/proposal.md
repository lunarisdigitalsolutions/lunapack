# Deny Lifecycle Scripts Proposal

## Why

Persisted trust can authorize lifecycle scripts, but consumers cannot persist a policy that categorically prevents script execution. Initialization also writes optional empty and default-valued YAML sections, creating configuration noise that obscures meaningful choices.

## What Changes

- Add a default-off `trust.deny.scripts` policy to project configuration, global-user trust, and project-local user trust.
- Make any applicable script-denial policy override source trust, pack trust, interactive confirmation, and invocation-level `--scripts run` for install, update, and uninstall.
- Continue lifecycle operations without policy-denied scripts, preserve instruction behavior, and warn for each denied script with the project or global scope that denied it.
- Extend trust commands and trust listing so users can set, clear, and inspect script denial in project, global-user, and project-local user scopes.
- Keep existing configuration compatible: omitted denial means scripts are not denied and retains current trust and `--scripts` behavior.
- Make `luna init` and `luna pack init` serialize only schema-required values and explicitly supplied values that do not equal their defaults, omitting optional empty collections and default-valued properties.
- Update product requirements, internal architecture and security guidance, and developer documentation for denial precedence, diagnostics, configuration shapes, and minimal initialization output.

## Capabilities

### New Capabilities

None.

### Modified Capabilities

- `cli-project-configuration`: Persist, manage, list, and clear script denial in all trust scopes, and initialize project YAML without optional default-valued sections.
- `local-pack-lifecycle`: Apply script denial before all other authorization paths, continue lifecycle work without denied scripts, and identify the denying scope in warnings and dry-run output.
- `manifest-schemas`: Define backward-compatible project and user-settings schema shapes for script denial and preserve validity when optional default-valued sections are omitted.
- `pack-authoring`: Initialize pack manifests with only required and explicitly supplied non-default values.

## Impact

- **Public contracts:** `lunapack.yml` and `~/.lunapack/config.yml` gain optional script-denial properties while retaining schema version `1`; generated `lunapack.yml` and `pack.yml` become smaller.
- **CLI:** Trust commands, trust listings, install, update, uninstall, dry-run diagnostics, `luna init`, and `luna pack init` change observable behavior.
- **Core implementation:** Project and user trust models, schema validation, authorization precedence, lifecycle warning presentation, YAML serialization, and initialization handlers are affected.
- **Compatibility:** Existing files remain valid. Missing denial is equivalent to `false`; explicit `false` is accepted but omitted from generated YAML. Persisted grants remain stored but ineffective while any applicable denial is active.
- **Documentation:** Product trust and lifecycle requirements, an accepted architecture decision, internal lifecycle-script safety guidance, and developer configuration, commands, lifecycle, and trust documentation require updates.
