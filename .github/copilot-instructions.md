# Decisions and Documentation

- For every decision made while working, assess its documentation impact. Update the relevant internal documentation in `docs/internal` and developer documentation in `docs/developer` in the same change whenever the decision creates, changes, or clarifies a fact that maintainers or consumers need to know.
- Keep implementation and public-consumption guidance separate. Do not document planned behavior as implemented.
- Create an ADR for each accepted, durable decision that affects architecture boundaries, lifecycle behavior, trust, compatibility, governance, repository conventions, or recurring engineering workflows. Do not create ADRs for transient or strictly local implementation details.
- Create ADRs in `docs/internal/architecture/adr` from `template.md`, assign the next sequential number, and add accepted records to `index.md`.
- Don't change existing ADRs. Rather create a new one that superseeds the old one and change the status for the old one to Superseded by <ADR-xxxx>.

# Changelog

Add only externally observable consumer changes to `CHANGELOG.md`. Exclude
internal maintenance work, including CI, build, test, and release-process
changes.

# CLI Path Handling

`ProjectPath` is the single path-canonicalization authority in
`projects/cli/src/Lunapack.Cli`.

- Use `ProjectPath.Normalize` or `NormalizeOptional` for pack, configuration,
  lock-file, glob, and repository-relative path values. It converts `\` to
  `/` only.
- Use `ProjectPath.NormalizeProjectRelativePath(fileSystem, projectDirectory,
path)` for CLI or other external input that identifies a project filesystem
  location. It resolves `.` and `..`, rejects empty, rooted, and escaping paths,
  and returns a slash-only project-relative path with no trailing slash.
- Never add another `Replace('\\', '/')` implementation. Apply any extra
  semantic trimming only after calling `ProjectPath`.
- Persist project configuration and lock-file paths with `/`; accept either
  separator at supported input boundaries. Do not normalize URLs or opaque
  identifiers as paths.
- Add focused tests for Windows-style input, canonical persisted output, and
  lock ownership matching when a path boundary changes. See
  `docs/internal/development/path-handling.md` and ADR-0037.

<!-- rtk-instructions v2 -->

# RTK — Token-Optimized CLI

**rtk** is a CLI proxy that filters and compresses command outputs, saving 60-90% tokens.

## Rule

Always prefix shell commands with `rtk`:

```bash
# Instead of:              Use:
git status                 rtk git status
git log -10                rtk git log -10
cargo test                 rtk cargo test
docker ps                  rtk docker ps
kubectl get pods           rtk kubectl get pods
```

## Meta commands (use directly)

```bash
rtk gain              # Token savings dashboard
rtk gain --history    # Per-command savings history
rtk discover          # Find missed rtk opportunities
rtk proxy <cmd>       # Run raw (no filtering) but track usage
```

<!-- /rtk-instructions -->

Terse like caveman. Technical substance exact. Only fluff die. ultra mode.
Drop: articles, filler (just/really/basically), pleasantries, hedging.
Fragments OK. Short synonyms. Code unchanged.
Pattern: [thing] [action] [reason]. [next step].
ACTIVE EVERY RESPONSE. No revert after many turns. No filler drift.
Code/commits/PRs: normal. Off: "stop caveman" / "normal mode".
