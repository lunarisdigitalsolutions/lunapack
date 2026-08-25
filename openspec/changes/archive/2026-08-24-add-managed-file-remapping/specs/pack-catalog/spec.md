## ADDED Requirements

### Requirement: Inspect managed-file targets with effective remapping

`luna inspect <pack-id>` SHALL display the pack's managed-file targets in a
dedicated, readable list without displaying their manifest source selectors. For
each target affected by valid global remapping in the inspected project's
`lunapack.yml`, the list SHALL display the declared target and the effective
target as `<declared-target> -> <effective-target>`. Targets unaffected by global
remapping SHALL display only their declared target.

#### Scenario: Inspect a pack with an ADR directory mapping

- **WHEN** a project globally remaps `docs/adr` and a consumer runs
  `luna inspect madr-adr-template`
- **THEN** inspection lists `docs/adr/template.md ->` followed by the remapped
  effective target, without listing `templates/template.md`

#### Scenario: Inspect a pack without matching remapping

- **WHEN** a pack's managed target does not match configured global remapping
- **THEN** inspection lists only that declared target
