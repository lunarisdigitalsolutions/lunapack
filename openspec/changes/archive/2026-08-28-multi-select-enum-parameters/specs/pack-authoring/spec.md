## MODIFIED Requirements

### Requirement: Maintain pack metadata, tags, and parameters

The CLI SHALL provide `set`, `list`, and `rm` operations as applicable for every
pack metadata property, tag, and parameter declaration accepted by the
published schema. It SHALL preserve value types, enum ordering, optional
display metadata, scalar-or-multi-select shape, and multi-select default order.

#### Scenario: Set scalar metadata

- **WHEN** an author runs `luna pack set description "ASP.NET API standards"`
- **THEN** LunaPack updates only the manifest description

#### Scenario: Maintain tags

- **WHEN** an author adds or removes a valid tag
- **THEN** LunaPack persists a unique tag collection within schema limits

#### Scenario: Maintain a parameter

- **WHEN** an author sets a string, boolean, scalar enum, or multi-select enum
  parameter with supported prompt metadata and default
- **THEN** LunaPack persists a schema-valid typed declaration without changing
  selection or declaration order

#### Scenario: Reject an invalid multi-select parameter

- **WHEN** an author sets `multiple` on a non-enum parameter or supplies an
  invalid or duplicate multi-select default
- **THEN** LunaPack rejects the operation and leaves `pack.yml` unchanged

#### Scenario: Protect required metadata

- **WHEN** an author attempts to remove required metadata or set an invalid
  version
- **THEN** LunaPack rejects the operation and leaves `pack.yml` unchanged
