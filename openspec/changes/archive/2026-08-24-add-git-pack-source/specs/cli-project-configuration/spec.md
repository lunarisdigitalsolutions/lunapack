## ADDED Requirements

### Requirement: Register a Git pack source

The `luna source add git <repository-url>` command SHALL add a Git source to the current directory's `lunapack.yml`. It SHALL accept optional `--ref <branch-or-commit>` and `--path <repository-relative-path>` arguments, retain the supplied values in the source entry, and reject a duplicate Git source with the same repository URL, ref, and path. It SHALL preserve existing configuration when the command arguments are invalid or the project state is invalid.

#### Scenario: Add a Git source with a branch and path

- **WHEN** a user runs `luna source add git <repository-url> --ref main --path packs` after initialization
- **THEN** `lunapack.yml` contains one Git source with the repository URL, `main` ref, and `packs` path

#### Scenario: Add a Git source with defaults

- **WHEN** a user runs `luna source add git <repository-url>` after initialization
- **THEN** `lunapack.yml` contains one Git source with that repository URL and no explicit ref or path

#### Scenario: Reject a duplicate Git source

- **WHEN** a user adds a Git source whose repository URL, ref, and path equal an existing configured Git source
- **THEN** LunaPack leaves project configuration unchanged and returns a non-success result
