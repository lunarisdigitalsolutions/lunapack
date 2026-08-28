# Remap and Move Work

- [ ] Inspect partial remap and `mv` implementation, command parsing, configuration model, lock lifecycle, and current tests. **In progress**
- [ ] Make `luna mv` move directory trees by remapping every managed file beneath the source while preserving relative paths.
- [ ] Add optional `--save-remap` to `luna mv` and persist generated remaps to `lunapack.yml`.
- [ ] Add optional `--save-remap` to `luna install` and persist supplied remaps to `lunapack.yml`.
- [ ] Add the `@ignore` special remap target for files and directories from CLI and `lunapack.yml`.
- [ ] Ensure install omits ignored pack files from disk and lock managed-file entries.
- [ ] Ensure update leaves ignored files unmanaged, restores newly unignored files, and handles existing managed files consistently.
- [ ] Add or revise unit and integration coverage for positive, negative, and edge cases across remap, move, install, update, persistence, and `@ignore`.
- [ ] Update consumer and maintainer documentation for directory moves, saved remaps, and `@ignore` semantics.
- [ ] Record durable remap lifecycle and special-target behavior in a new ADR and update the ADR index.
- [ ] Add externally observable changes to `CHANGELOG.md`.
- [ ] Run focused tests after each implementation slice, then full relevant CLI validation.
