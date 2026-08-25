## Context

See [proposal.md](proposal.md) for the change motivation. Version 1 manifests
can describe only pack identity, version, optional description, and complete
managed-file mappings. Installation copies only to absent targets, records a
digest for each copied file, and removal rejects modified files. Source paths
in `lunapack.yml` are resolved relative to the consumer project directory.

## Goals / Non-Goals

**Goals:**

- Turn conventions used by this repository into a small, discoverable local
  catalog.
- Exercise catalog behavior from the repository root without claiming
  ownership of files that already exist there.
- Keep every pack independently understandable, except for the coupled .NET
  quality baseline files.

**Non-Goals:**

- Add merge, overwrite, synchronization, dependency-resolution, validation,
  or parent-directory-creation behavior.
- Install bundled packs into this repository or change the version-1 schemas
  and CLI command surface.
- Package repository-relative Copilot guidance, Husky hooks, or the Node
  toolchain as part of this change.

## Decisions

### Publish a narrow catalog with one intentional composite pack

Create independent packs for the Git ignore, SDK pin, EditorConfig file,
CSharpier tool manifest, and MADR template. Create one `dotnet-quality-baseline`
pack for `Directory.Build.props` and `Directory.Packages.props` because shared
build policy references centrally managed package versions.

This keeps a typical pack to one owned file while preventing consumers from
installing an incomplete .NET quality policy. Splitting the props files was
rejected because it would make partial configuration easy to install. A broad
repository bootstrap pack was rejected because version 1 cannot express its
runtime and tooling dependencies safely.

### Make the repository an installed consumer with explicit overlays

Add root `lunapack.yml` with schema version 1 and a relative `projects/packs`
local source. Remove the existing targets, install every bundled pack, then
restore repository-specific content manually. Relative configuration makes the
source valid for any checkout location.

The root will deliberately diverge from the recorded digests for `.gitignore`
and `Directory.Packages.props`: the ignore file retains Node, documentation,
and Husky exclusions, while the central package list restores CLI-specific
versions. A later lifecycle update or uninstall will reject those changed
managed files until the repository reconciles them.

### Preserve version-1 file constraints in catalog content and guidance

Every pack maps immutable source content to complete target files. The MADR
pack targets `docs/adr/template.md`; consumers create `docs/adr` before
installing because version 1 does not create parent directories. Developer
documentation will state target paths and prerequisites rather than implying
file merging or directory provisioning.

Targeting an existing root-level template was rejected because it has poor
consumer organization. Extending the lifecycle to create directories was
rejected because it expands the current contract and is not required for
dogfooding existing behavior.

### Document a durable installed-consumer convention

Create an ADR that supersedes the source-only decision and records
`projects/packs` as the repository's first-party local catalog and installed
consumer pattern. Update product documentation to show the MVP has evolved from
a single sample pack to a reusable bundled catalog; update internal
documentation for the ownership boundary; and update developer documentation
with catalog and consumer guidance.

## Risks / Trade-offs

- [Version-1 does not create target directories] -> Document the MADR pack
  prerequisite and validate installation only after a fixture creates `docs/adr`.
- [Repository overlays change managed content] -> Keep the recorded pack state,
  document that uninstall and future updates reject changed targets, and
  reconcile overlays before either operation.
- [Bundled templates may drift from repository conventions] -> Derive pack
  content from the existing files and update the relevant pack version whenever
  the convention changes.
- [The props pair is less granular than the other packs] -> Keep it as the only
  composite pack and document why both files are required.

## Migration Plan

1. Remove root managed targets, install the catalog, and manually restore the
   repository-specific overlays.
2. Verify discovery, installed-pack state, and lifecycle behavior in isolated
   consumer workspaces.
3. Roll back by reconciling or uninstalling unchanged managed content, then
   restoring the previous root files and manifest.
