# Design

## Context

See `proposal.md` for motivation and the change's specifications. Discover,
search, and inspect share catalog services, while dependency resolution needs
access to catalog releases that user-facing listings may hide. Search also
returns link results. The CLI currently applies `--suppress-next-steps` through
the shared renderer, and persists user settings in `~/.lunapack/config.yml`.
Project preferences belong in `lunapack.yml`; the user settings store already
has canonical project-directory keys for per-project data.

## Goals / Non-Goals

**Goals:**

- Keep display and direct-root eligibility filters separate from complete
  catalog resolution.
- Resolve each preference consistently across all commands and scopes.
- Preserve existing project configuration and user trust data when adding
  preferences.
- Keep current manifests and settings valid without a schema-version bump.

**Non-Goals:**

- Treat `directInstallable` as a security boundary or prevent older CLI
  versions from installing a pack that declares it `false`.
- Hide dependency-only packs from validation, inspection, or dependency
  resolution.
- Add a switch that reveals dependency-only packs in discover or search, or
  make search's existing link results subject to pack filters.

## Decisions

### Keep direct-installability independent of pack composition

Represent installability as an optional Boolean manifest property with an
explicit `true` default in the manifest model. Continue deriving raw versus
composite status from whether `packs` references are declared; neither concept
implies the other.

Apply the installability check to directly requested roots and to catalog
presentation. For discover and search, apply it before composite filtering,
version selection, and relevance ranking. For install, update, and outdated
root selection, apply it before selecting a release. Keep dependency graph
resolution on the complete catalog so a directly-installable composite can
consume a dependency-only pack. Validation and explicit inspection also use
the complete catalog; inspection reports the installability value.

Filtering releases before selection permits an older directly-installable
release to remain visible or selectable when a newer release is dependency-only.
Filtering only after choosing the latest release could incorrectly hide the
pack entirely. Applying the filter to the catalog service itself would also
break dependency resolution and explicit inspection, so the restriction
belongs at presentation and root-selection boundaries.

### Share catalog filtering without changing unrelated results

Use the same eligibility and composition predicates for discover and search.
The `--only-composites` flag enables the composite filter for one invocation;
an enabled command-specific preference does the same by default. The flag is
one-way, so users disable a persisted filter by setting the preference to
`false`. Search applies pack filters before ranking and leaves link results
unchanged. `--allow-draft` does not bypass the installability restriction.

Keep the full catalog available to inspect, validation, and dependency
resolution rather than implementing hidden-pack visibility as a global catalog
setting. This avoids separate catalog modes and makes the filter's scope
explicit.

### Extend existing preference stores with distinct preference data

Store project-local values in an optional `preferences` object in
`lunapack.yml`. Store user-global and user-project values in a preferences
section of the existing `UserSettingsStore` document, separate from trust
grants and denials. Key user-project values by the existing canonical absolute
project-directory identity. Use typed Boolean values for the three supported
keys; map the dotted CLI names to the schema's nested YAML properties.

`luna config` reads, writes, lists, and removes only values in its selected
scope. Scope options are mutually exclusive, with global as the default. One
shared resolver applies local, user-project, global, and built-in-default
precedence. Updates must use the existing read-modify-write path so trust data
is retained. Reject unknown keys, non-Boolean values, and non-canonical
project keys before mutation.

### Apply guidance preference at invocation setup

Resolve `next-steps.enabled` through the same preference resolver and combine
it with the existing invocation-level suppression before recommendations are
rendered. Keep recommendation selection and rendering behavior otherwise
unchanged. The explicit `--suppress-next-steps` option always wins for its
invocation; an unset preference preserves today's enabled behavior.

### Preserve omission compatibility and document old-client limits

Keep `directInstallable` and configuration preferences optional. Omitted
manifest metadata means `true`; omitted preferences use built-in defaults.
These additions do not require a schema-version change. Existing CLI readers
may ignore or reject fields they do not recognize, and older writers may not
preserve new preference fields. In particular, older CLIs may still install a
pack marked `directInstallable: false`; document this as a compatibility
limitation, not an access-control guarantee. ADR-0074 remains the compatibility
reference.

## Risks / Trade-offs

- Older CLI versions may ignore `directInstallable: false` or rewrite settings
  without retaining new preference fields → document supported-version
  expectations and do not present installability metadata as security control.
- Duplicated scope or catalog-filter logic could make discover and search
  disagree → route both commands through shared preference resolution and
  eligibility/composition predicates.
- Writing preferences could overwrite trust data in the shared user settings
  file → keep preference data distinct and test read-modify-write preservation.
- Filtering before version selection can expose older eligible releases →
  make the rule explicit and cover it with catalog and root-selection tests.

## Migration Plan

No automatic migration is required. Existing pack manifests and project
configurations remain valid when the new optional fields are absent. New
configuration values are written only when set; built-in defaults apply when
they are absent. No rollback data conversion is needed, but older CLI versions
may not preserve newly written preferences or enforce direct-installability.
