## Context

See [proposal.md](proposal.md) for the motivation and the delta specifications
for behavioral contracts. Today, `lunapack install` resolves every managed-file
target exactly as a pack declares it. `lunapack.yml` records only an ID and
optional version for a direct request; `lunapack-lock.yml` records effective
managed targets and digests but no installation destination. Existing target
protection correctly refuses to claim user-owned files.

The repository has no CI/release workflows. Its root
`Directory.Build.props` and the `dotnet-build-props` template already support
some build policy but need the requested CI determinism, Source Link, and
MinVer settings. The requested documentation sources are
`docs/internal/development/coding-guidelines/csharp.md` and
`docs/internal/development/coding-guidelines/clean-code.md`; their text is not
reproduced here.

## Goals / Non-Goals

**Goals:**

- Persist a safe, direct-pack installation destination in portable and resolved
  state without invalidating version-1 files that omit it.
- Let this repository adopt its matching existing guidance files only through
  explicit, digest-verified consent.
- Reuse one CLI build composite action from tag and pull-request CI while
  keeping the tag-release workflow's two-job `build` and `release` shape.
- Build tag-traceable archives on matching operating-system runners, preserve
  validation evidence, and release checksum-verified assets.

**Non-Goals:**

- Add remote pack sources, destination templates, arbitrary per-file target
  remapping, or relocation of dependency-managed files.
- Overwrite or silently claim an existing unowned file.
- Build unsupported macOS 32-bit binaries, code-sign/notarize releases, or
  publish package-manager artifacts.
- Create a release from a branch push, a workflow run number, or a generated
  tag.
- Create a GitHub Actions-guidelines pack; the user explicitly removed it from
  this change.

## Decisions

### Persist a destination on the direct installation request

`lunapack install <pack-id> --destination <relative-directory>` treats the input
as the root directory for targets owned by that direct request. A file whose
pack target is `clean-code.md`, for example, becomes
`<destination>/clean-code.md`. A request without the option retains the pack
manifest target unchanged.

The command will validate the destination as non-empty, relative, and confined
to the project directory before resolving or copying files. The installation
request will carry an explicit destination value through command parsing,
resolution, planning, state persistence, and uninstall. The requested-pack
entry in `lunapack.yml` and its corresponding direct resolved-pack entry in
`lunapack-lock.yml` will contain the optional `destination`; the lock file
continues to own the authoritative effective target paths and digests.

Only files declared by the directly requested pack are relocated. Dependencies
keep their declared targets. This prevents a shared dependency from gaining
different target paths based on which composite pack introduced it and preserves
the existing graph-resolution model.

Alternatives considered:

- Store a replacement target on each pack managed-file declaration: rejected
  because it mutates immutable pack policy in every consumer state file and
  turns one directory choice into an unbounded mapping surface.
- Relocate the entire resolved dependency graph: rejected because shared
  dependencies would have ambiguous ownership and uninstall behavior.
- Change a pack manifest's target before each install: rejected because it
  alters the published pack rather than recording the consumer's selection.

### Require explicit digest-matched adoption

`--adopt-existing` gives the caller a narrow way to establish management of
the repository's existing documentation. Before any copy or state write, the
planner will compare every unowned target selected for adoption to the
corresponding source content's SHA-256 digest. All comparisons must pass before
the lifecycle transaction records any ownership. A mismatch retains the
current failure behavior and leaves files and both state files intact.

The command will use an installation request value rather than expanding
Boolean parameters through planning and lifecycle methods. The value captures
the pack reference, optional destination, and explicit adoption intent as one
validated boundary input.

An unconditional `--force` option was rejected because it would bypass the
repository's user-owned-target safety guarantee. Automatic same-content
adoption was rejected because observing an identical file is not consent to
manage it.

### Build generic documentation packs, then dogfood them

Create `projects/packs/csharp-guidelines` and
`projects/packs/clean-code-guidelines`, each with a `pack.yml` and one
Markdown template. Derive the templates from the source documents named in the
Context after removing or generalizing LunaPack-specific content. Update those
repository documents to the same portable text so their content hashes match
the new templates.

Add both packs to this repository's `lunapack.yml`, using the coding-guidelines
directory as their destinations, and install with `--adopt-existing`. The
resulting `lunapack-lock.yml` records the direct destinations, effective paths,
and digests. No guideline text belongs in OpenSpec artifacts: implementation
will use the documented source paths as its content authority.

Separate packs, rather than one composite, let consumers adopt either
convention independently. A composite was rejected because it would not meet
the requested independent C# and clean-code pack use cases.

### Implement composite action layers

Place the generic build action at `.github/actions/build-dotnet/action.yml`.
It sets up the SDK, restores locked dependencies, builds and tests the supplied
solution with coverage, and optionally publishes a project. Its
whitespace-delimited additional publish argument string keeps AOT and
self-contained output caller-selected. It uploads test and coverage artifacts
when requested and lists published files in the workflow summary.

Place CLI build and release actions at `.github/actions/cli/build/action.yml`
and `.github/actions/cli/release/action.yml`. The build action composes the
generic action, restores repository-local tools, resolves a MinVer version or
accepts an override, creates ZIP archives on Windows and tar.gz archives on
Unix hosts, and uploads RID-qualified archives. Linux x86 is hostless
framework-dependent because the pinned SDK lacks a compatible application host
and Native AOT runtime pack. The release action downloads archives, generates
checksums, stages `CHANGELOG.md`, and creates the existing GitHub Release.

`.github/workflows/cli.yml` owns the matrix because composite actions cannot
declare one. A planning job produces its dynamic matrix. `releases/*` tag
pushes require a semantic suffix, select Windows and Linux x86/x64 rows, and
use that suffix as the build and archive version while preserving the full tag
for the GitHub Release. Manual dispatch accepts a comma-separated list of
supported runtime identifiers, builds only those targets, and leaves the
version override empty for MinVer. Manual builds do not create GitHub Releases.

`.github/workflows/cli-pr.yml` checks out complete history and invokes the same
CLI build action on Linux x64 with artifact publication disabled. It has no
release job.

### Publish traceable releases with GitHub-native credentials

The release job runs on Ubuntu only after all required tag build artifacts
succeed. It downloads every distribution archive, generates one sorted
SHA-256 manifest, and uses the repository's `GITHUB_TOKEN` with
`contents: write` permission to create a GitHub Release for the existing tag.
It copies root `CHANGELOG.md` to the release staging directory and uploads it
as a release asset with the archives and checksum file. Release asset names use
the pushed tag and RID.

Using the GitHub CLI or REST API with the ephemeral token avoids a separate
long-lived secret. A third-party release action was rejected because the native
GitHub mechanism is sufficient and keeps the release trust boundary smaller.

### Apply one deterministic CI policy to the repository and pack template

Update the root `Directory.Build.props` and
`projects/packs/dotnet-build-props/templates/Directory.Build.props` together.
When `CI` is `true`, both set `Deterministic`, `EnableSourceLink`, and
`ContinuousIntegrationBuild`. Both always disable source revision content in
the informational version, enable package lock-file generation, use locked
restore for continuous-integration builds, and set
`MinVerDefaultPreReleaseIdentifiers` to `preview`.

Add the MinVer package as a private CLI build dependency through central package
management and add `minver-cli` to the repository tool manifest. The MinVer
package owns standard assembly, file, informational, package, and semantic
version mapping. The CLI build action uses the restored tool's calculated
version as `MINVERVERSIONOVERRIDE`, ensuring the build package and external
release assets share one tag-derived value. The implementation will verify
whether the installed SDK supplies the required GitHub Source Link provider and
add a centrally versioned provider package only if the build demonstrates that
it is required.

## Risks / Trade-offs

- [A Linux x86 application host is unavailable] -> Publish hostless
  framework-dependent output and reject AOT or self-contained arguments for
  that row.
- [A manually selected runtime cannot publish with the supplied arguments] ->
  Validate the selected target set and optional argument string before relying
  on a release tag.
- [A shallow checkout cannot find the intended release tag] -> Fetch full
  history and tags before MinVer runs.
- [A pushed tag differs from MinVer] -> Use the tag as the explicit release
  version without a MinVer comparison.
- [A guideline document changes before adoption] -> Digest comparison fails
  atomically; align the generic source and template before running adoption.
- [Per-target tests increase CI duration] -> Preserve the required validation
  on every production target; refine only after evidence supports a separate
  test strategy.
- [Coverage reporters differ across .NET test platform versions] -> Validate
  the selected command and artifact outputs on the supported .NET 10 SDK before
  finalizing the composite action.

## Migration Plan

1. Extend the configuration models, schemas, parser, lifecycle transaction,
   lock construction, and focused unit/integration/schema coverage for
   destination and adoption.
2. Create the two pack manifests and templates; make the source guidance
   documents portable and byte-identical to their templates; request and adopt
   both packs in the repository state files.
3. Add MinVer's centrally versioned CLI dependency and local tool, apply
   deterministic CI and MinVer properties to the root and pack template, then
   verify local restore, formatting, build, test, and manifest validation.
4. Add generic and CLI composite actions, a workflow-owned dynamic matrix, tag
   and pull-request callers, tag version propagation, changelog staging,
   checksum generation, and release behavior.
5. Update developer and internal documentation, record the lock-validation
   boundary now retained in ADR-0023, and validate the full OpenSpec change
   plus repository quality gates.

Rollback reverts the workflow and build-property changes without touching
released assets. Before returning to a CLI version that does not recognize
`destination`, remove the two adopted pack records and their managed files or
restore the repository's committed `lunapack.yml` and `lunapack-lock.yml` together;
this avoids loading newer state through an older schema.
