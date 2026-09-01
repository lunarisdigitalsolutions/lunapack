---
status: accepted
date: 2026-09-01
decision-makers: LunaPack maintainers
---

# ADR-0074: Evolve YAML Schemas Compatibly

## Context and Problem Statement

Projects and pack catalogs persist `lunapack.yml`, `lunapack-lock.yml`, and
`pack.yml` across CLI upgrades. Even additive optional fields can prevent an
older CLI from reading a newer document when deserialization rejects unknown
properties. Incompatible changes can also strand older valid documents unless
the newer CLI understands and migrates their schema version.

## Decision Drivers

- Let older CLI releases read documents containing newer optional fields.
- Preserve omission-compatible defaults for fields an older CLI cannot know.
- Make incompatible document changes explicit and recoverable.
- Avoid silently reinterpreting an existing schema version.

## Considered Options

- Reject every unknown YAML property.
- Ignore unknown properties without governing incompatible changes.
- Ignore optional additions and require versioned migrations for incompatible
  changes.

## Decision Outcome

Chosen option: "Ignore optional additions and require versioned migrations for
incompatible changes," because it preserves forward readability while keeping
semantic changes explicit.

All CLI read paths for the three persisted YAML contracts ignore unknown
properties, including unknown nested values handled by custom converters. An
omitted known property continues to use its model default. Schema validation
remains strict for documents authored or validated against the current schema.

Adding an optional property whose omission preserves existing behavior does not
increment the schema version. Adding a required property, renaming or removing
a property, changing its meaning, or invalidating previously accepted values is
incompatible. Maintainers must warn before release, increment the affected
schema version, and ship a migration layer that reads each supported old model
and converts it to the current model before use. The next successful persisted
update writes the current format.

`lunapack.yml` and `lunapack-lock.yml` use their existing explicit
`schemaVersion`. `pack.yml` is implicitly version 1; its first incompatible
revision must introduce an explicit discriminator while recognizing an absent
discriminator as version 1.

This decision governs future schema evolution. It does not retrofit migration
support for obsolete prerelease formats intentionally rejected by ADR-0016.

### Consequences

- Older CLIs retain known behavior when newer optional fields appear.
- New CLIs must retain tested old-version readers for supported migrations.
- Breaking schema work carries advance communication and migration cost.
- Current-schema validation can still reject misspelled or unsupported fields
  even though runtime reads tolerate fields introduced by newer CLIs.

### Confirmation

Parser tests cover unknown scalar, sequence, and mapping values at normal and
custom-converter boundaries. Every future incompatible change includes old
fixtures, migration tests, current-format rewrite tests, and rejection tests for
unsupported future versions.

## More Information

- [JSON Schema coding guidelines](../../development/coding-guidelines/json-schema.md)
- [ADR-0016](0016-split-portable-configuration-from-resolved-lock-state.md)
