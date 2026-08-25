## 1. Configuration and Lock Contracts

- [x] 1.1 Extend the project configuration model and `projects/schema/lunapack.schema.json` with validated optional `remap.directories` and `remap.files` mappings; add configuration parsing and schema-validation tests for valid, unsafe, and precedence cases.
- [x] 1.2 Add a canonical target-remapping resolver shared by configuration, installation requests, and planning; parse repeatable `from=to` install mappings, reject duplicate/unsafe mappings and `--destination` combinations, and cover request validation tests.
- [x] 1.3 Define `ProjectLockFile` and lock serialization schema version 1 with required declared and effective target identities; reject unreleased legacy shapes and add fixture tests.
- [x] 1.4 Add `luna remap set <directory|file> <target> <newTarget>` to validate, normalize, and upsert global remapping configuration without relocating lock-managed files; add `list` and `rm`; cover directory, file, invalid input, and real-process lifecycle flows.
- [x] 1.5 Canonicalize configuration and lock-file path serialization to forward slashes while accepting Windows-style project, pack, and CLI path input across supported operating systems; cover raw state, command, and lifecycle paths.

## 2. Lifecycle Resolution and Relocation

- [x] 2.1 Apply target remapping before preflight in `PackInstallationPlanner`, including file-over-directory and command-line-over-global precedence, suffix preservation, collision/adoption handling, and dry-run output; add focused planner tests.
- [x] 2.2 Update install, update, and uninstall lifecycle flows to associate retained files by declared target identity and operate at their recorded effective targets; apply current global remaps only to newly introduced files and test transactional rollback paths.
- [x] 2.3 Add repeatable `--remap-directory` and `--remap-file` options to `luna install`, route them to the lifecycle request, and add CLI command coverage for directory and single-file remapping.
- [x] 2.4 Add `luna mv <source> <target>` command registration, handler, and lifecycle operation; move or rebind one uniquely owned lock record atomically, preserve recorded digests, reject unsafe/ambiguous ownership and filesystem states, and test rollback on lock persistence failure.

## 3. Inspect Output

- [x] 3.1 Pass project remapping context through `luna inspect` and render a managed-files table containing targets only, with `declared -> effective` output for matching global mappings; add formatter and command-handler tests.

## 4. Documentation and Governance

- [x] 4.1 Update developer pack installation guidance and the pack manifest reference with global `remap` YAML, installation option syntax, precedence, `luna mv`, and lock-backed update/uninstall behavior.
- [x] 4.2 Update `docs/product/prd/003-pack-lifecycle.md` with consumer managed-file remapping and relocation behavior.
- [x] 4.3 Create the next sequential accepted ADR from `docs/internal/architecture/adr/template.md`, add it to the ADR index, and document declared/effective lock identities, version migration, and the explicit-move policy.

## 5. Verification

- [x] 5.1 Run CSharpier on touched CLI files, validate JSON schemas and Markdown links, then run focused unit tests for configuration, installation planning, lifecycle/update/uninstall, move, and inspection.
- [x] 5.2 Run the CLI integration suite covering remapped install, update, uninstall, inspect, and both `luna mv` success paths; run the locked restore and AOT CLI build before completing the change.
