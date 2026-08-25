# JSON Schema Coding Guidelines

This reference governs the version-1 schemas for `lunapack.yml`, `lunapack-lock.yml`,
and `pack.yml`. They define the CLI's machine-readable contracts.

Use JSON Schema draft 7 and give every published schema a stable `$id` under
`https://lunapack.dev/schema/`. Define object contracts with
`additionalProperties: false`, explicit `required` fields, and constraints that
reject invalid values at the boundary.

Keep the project-manifest `schemaVersion` explicit. Do not reinterpret version
`1` documents: an incompatible change requires a new schema version and an
updated compatibility specification. Test every schema behavior change with a
valid and rejected document where applicable.

Update the schema reference when a contract changes. Format JSON with the
repository formatter and validate the affected .NET test suite before merging.
