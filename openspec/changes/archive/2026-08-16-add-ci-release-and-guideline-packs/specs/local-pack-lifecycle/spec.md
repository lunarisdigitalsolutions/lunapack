## ADDED Requirements

### Requirement: Install a directly requested pack at a destination

LunaPack SHALL accept `lunapack install <pack-id> --destination <relative-directory>`
and apply the directory to every managed-file target owned directly by the
requested pack. It SHALL retain a pack manifest's declared targets when the
option is omitted. The destination SHALL be a non-empty path relative to the
project directory and SHALL not resolve outside it.

Dependencies resolved for the requested pack SHALL retain their declared
targets; the destination SHALL not relocate their managed files.

#### Scenario: Install a documentation pack into a selected directory

- **WHEN** a consumer installs a single-file documentation pack with
  `--destination docs/guidance`
- **THEN** LunaPack writes that file beneath `docs/guidance` and records its
  effective project-relative target

#### Scenario: Reject an unsafe destination

- **WHEN** a consumer supplies an absolute destination or one that resolves
  outside the project directory
- **THEN** LunaPack returns a non-success result and leaves project files and
  manifest state unchanged

#### Scenario: Preserve dependency targets

- **WHEN** a consumer installs a composite pack with a destination
- **THEN** any dependency-managed file uses its declared target rather than the
  composite pack's destination

### Requirement: Adopt matching existing pack targets explicitly

LunaPack SHALL accept `--adopt-existing` during installation. It SHALL adopt an
existing unowned target only when every target to adopt has the exact SHA-256
digest of the source content that the pack would install. Without this option,
LunaPack SHALL retain its existing refusal to claim unowned targets.

If any proposed target differs from the source digest, LunaPack SHALL not copy,
delete, or claim any target and SHALL leave `lunapack.yml` and `lunapack-lock.yml`
unchanged.

#### Scenario: Adopt unchanged documentation

- **WHEN** a consumer installs a documentation pack with `--adopt-existing`
  and the existing target has the pack-content digest
- **THEN** LunaPack records ownership without replacing the target content

#### Scenario: Reject adoption of modified documentation

- **WHEN** a consumer installs a documentation pack with `--adopt-existing`
  and an existing target differs from the pack-content digest
- **THEN** LunaPack returns a non-success result without changing files or
  installation state

### Requirement: Persist destination selection

LunaPack SHALL preserve a directly requested pack's optional destination in both
`lunapack.yml` and `lunapack-lock.yml`, together with its resolved effective target
paths. A later uninstall SHALL remove the recorded effective targets after its
existing digest-protection checks pass.

#### Scenario: Remove a destination-installed pack

- **WHEN** a consumer uninstalls a destination-installed pack whose managed
  file still matches its recorded digest
- **THEN** LunaPack removes that file from its effective destination and removes
  the pack's destination metadata from both state files
