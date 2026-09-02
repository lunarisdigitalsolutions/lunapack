# JSON Schema Coding Guidelines

This reference governs the version-1 schemas for `lunapack.yml`, `lunapack-lock.yml`,
and `pack.yml`. They define the CLI's machine-readable contracts.

Use JSON Schema draft 7 and give every published schema a stable `$id` under
`https://lunapack.dev/schema/`. Define object contracts with
`additionalProperties: false`, explicit `required` fields, and constraints that
reject invalid values at the boundary.

Treat changes to `lunapack.yml`, `lunapack-lock.yml`, and `pack.yml` as persisted
contract changes. Adding an optional property with an omission-compatible
default is backward compatible: keep the schema version unchanged, and ensure
older CLI releases ignore that property when reading YAML. Test both omission
and unknown-property reads, including properties nested inside custom YAML
converters.

Warn maintainers and consumers before releasing an incompatible schema change.
Adding a required property, renaming or removing a property, changing its
meaning, or rejecting a previously valid value is incompatible. Such a change
must:

- increment the affected document's schema version;
- retain a reader for every supported older version;
- migrate the old model to the current model before normal validation and use;
- rewrite that document in the current format on its next successful persisted
  update; and
- include fixtures and tests for old reads, migration, rewritten output, and
  unsupported future versions.

`lunapack.yml` and `lunapack-lock.yml` carry explicit `schemaVersion` values.
`pack.yml` is currently implicitly version 1. Its first incompatible revision
must add an explicit discriminator and treat a missing discriminator as version
1 during migration. Do not reinterpret a versioned document in place. See
[ADR-0074](../../architecture/adr/0074-evolve-yaml-schemas-compatibly.md).

Optional trust restrictions may extend version 1 when omission preserves prior
behavior. `deny.scripts` defaults to false, grant collections may be omitted,
and acknowledgement records remain positive-only. Cover omission, explicit
false, denial-only records, retained explicit empty collections, and rejection
of denial under acknowledgements in schema and runtime-store tests.

Initialization output is a separate contract from normal persistence. Use
structured, Native AOT-registered projections for required properties instead
of hand-built YAML or global serializer changes. Validate exact generated YAML
against published schemas, load it through normal stores, and verify a later
mutation succeeds without changing established store serialization broadly.

Update the schema reference when a contract changes. Format JSON with the
repository formatter and validate the affected .NET test suite before merging.
