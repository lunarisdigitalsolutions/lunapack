## ADDED Requirements

### Requirement: Guide successful catalog exploration

Successful catalog commands SHALL report their result before rendering
recommendations. Recommendations SHALL use the inspected pack ID when known and
replacement tokens when no single concrete value is available.

#### Scenario: Discover available packs

- **WHEN** `luna discover` successfully displays one or more pack releases
- **THEN** Luna reports the displayed pack count and recommends `luna install
<pack>`

#### Scenario: Search available packs

- **WHEN** `luna search <keyword>` successfully displays one or more matching
  pack releases
- **THEN** Luna reports the displayed match count and recommends `luna inspect
<pack>` and `luna install <pack>`

#### Scenario: Inspect a pack

- **WHEN** `luna inspect <pack-reference>` successfully displays pack details
- **THEN** Luna recommends installing that resolved pack ID and running `luna
discover`

### Requirement: Guide recovery from an unresolved catalog pack

When inspection cannot resolve a syntactically valid pack reference from
configured sources, Luna SHALL preserve the primary non-success result and
append commands that help find an available pack.

#### Scenario: Inspect an unknown pack

- **WHEN** a user runs `luna inspect unknown-pack` and no configured source
  provides that pack
- **THEN** Luna reports that the pack was not found and recommends `luna search
unknown-pack` followed by `luna discover`
