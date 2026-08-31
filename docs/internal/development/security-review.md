# Release Security Review

This maintainer register tracks security work for LunaPack's first public
release. It complements the public [threat model](../../developer/threat-model.md)
without recording secret values or unnecessary exploitation detail.

## Release Risks

| ID      | Finding                                                                                       | Status            | Required verification                                   |
| ------- | --------------------------------------------------------------------------------------------- | ----------------- | ------------------------------------------------------- |
| SEC-003 | Container image has not been built or scanned because the local Docker backend is unavailable | Accepted deferral | Validate before a later container-focused release       |
| SEC-004 | npm, NuGet, and GHCR trusted-publisher settings cannot be verified from the working tree      | Maintainer review | Verify repository and registry settings before tagging  |
| SEC-005 | Four non-Windows NuGet and npm installation paths need final smoke tests                      | Open              | Pack, inspect, install, run help/version, and uninstall |
| SEC-006 | Final package, image, workflow, and squashed-history scans remain outstanding                 | Accepted deferral | Run before a later security-focused release             |

Items marked `Open`, `Maintainer review`, or `Environment blocked` keep final
status at `NOT READY`. Accepted deferrals remain explicit residual risks.

## Completed Controls

- Release targets are limited consistently to Windows x64, Linux x64, Linux
  Arm64, macOS x64, and macOS Arm64 and build on matching native runners.
- Public history will contain one reviewed squashed release commit; scan that
  resulting history before changing repository visibility.
- Third-party release actions use verified full commit SHAs.
- npm uses OIDC trusted publishing with provenance instead of a stored token.
- NuGet uses workload identity to obtain a temporary publishing key.
- Registry credentials are scoped to their publishing steps.
- Workflow-level permissions are read-only; only release and NuGet preview
  publication jobs receive OIDC or registry-write permissions.
- Release dry-run mode stages and validates artifacts without authentication or
  publication.
- Package staging rejects unsafe archive paths, links, special entries, and
  duplicates before extraction and rechecks extracted object types.
- The OCI image uses a digest-pinned runtime-only base, a non-root user, and an
  existing Linux Native AOT build.
- Configuration, pack, and lock documents expose one public schema version.
- Lock records require configured source identity and declared/effective target
  paths.
- Pack-defined Git URLs reject embedded credentials. Source prompts, lock
  records, fingerprints, audit output, and diagnostics use sanitized canonical
  identities.
- External source consent is graph-wide, defaults to no, and remains separate
  from lifecycle-script trust. `--accept-sources` cannot resolve identifier
  conflicts or bypass path, authentication, trust, or transaction checks.
- External content resolves to an immutable commit in a private operation
  directory. Sparse selection and followed links must remain below the approved
  source root, and external files are never executed as lifecycle scripts.
- Lifecycle processes use direct argument lists and explicit trust decisions.
- Release reruns download and byte-compare the exact expected GitHub Release
  assets and compare release notes before registry publication resumes.
- NuGet reports no vulnerable direct or transitive CLI or test dependencies.
- Root production npm dependencies report no vulnerabilities. Current-tree and
  visible-history high-confidence secret-signature scans report no matches.

## Deferred Hardening

These items are accepted as nonblocking for the first release. Current sources
are local folders or Git repositories selected by the consumer, lifecycle code
requires explicit trust, and no hosted pack catalog exists. Reassess priority
before broadening source trust, adding a catalog, or changing the execution
model.

- **SEC-101: Snapshot object types.** A trusted or compromised source can make
  snapshot copying follow an unintended filesystem entry. Schedule with future
  source hardening.
- **SEC-102: Resource budgets.** An unusually large or crafted trusted pack can
  exhaust memory, CPU, or disk. Schedule with future availability hardening.
- **SEC-103: Lifecycle environment.** An approved hook receives ambient
  environment values, which may include secrets. Schedule with a future
  lifecycle-contract revision.
- **SEC-104: No-follow and race confinement.** A same-user process can replace
  an entry between path validation and use. Schedule with advanced filesystem
  hardening.
- **SEC-105: OCI digest verification.** A mutable image tag can later identify
  bytes different from those first released. Schedule with future OCI
  supply-chain hardening.
- **SEC-106: Catalog publisher signatures.** A future catalog without signed
  identity could permit publisher substitution or metadata rollback. Require
  this work when designing a hosted catalog.

### Implementation Direction

#### SEC-101: Snapshot Object Types

- Replace recursive source enumeration with deterministic, one-level traversal.
- Inspect each entry before descent or copy and reject symbolic links, Windows
  junctions and reparse points, mount-like entries, devices, sockets, pipes,
  and other non-regular files.
- Resolve each destination beneath the private snapshot root, remove partial
  snapshots on failure, and report the rejected repository-relative path.
- Add real-filesystem tests for file links, directory links, junctions, escape
  attempts, normal nested trees, and cleanup on Windows, Linux, and macOS.

This first step narrows the known exposure but does not claim race-free
traversal. SEC-104 owns that stronger guarantee.

#### SEC-102: Resource Budgets

- Introduce one immutable `PackResourceLimits` policy shared by document
  readers, source discovery, graph resolution, snapshotting, installation
  planning, and template rendering.
- Bound YAML bytes and nesting, packs discovered per source, graph depth and
  nodes, managed-file declarations, selected file count, individual and total
  bytes, template input, rendered output, parameter count, and parameter length.
- Check size before allocation, use bounded streams because files can change,
  count during enumeration, use checked cumulative arithmetic, and stop at the
  first exceeded limit.
- Render through a bounded writer rather than creating an unlimited string.
  Verify exact-boundary success, boundary-plus-one failure, deterministic
  diagnostics, overflow resistance, and unchanged project state after failure.

Initial limits should be fixed, documented, and generous. Do not make security
ceilings project-controlled without defining a separate administrative policy.

#### SEC-103: Lifecycle Environment Contract

- Define a platform-specific `LifecycleEnvironmentPolicy` and clear
  `ProcessStartInfo.Environment` before adding its allowlist.
- Resolve the executable before clearing the environment. Preserve only values
  required for process startup, command lookup, locale, and temporary storage;
  do not forward credentials, cloud tokens, CI variables, or arbitrary parent
  values.
- Decide explicitly whether compatibility variables such as `HOME`,
  `USERPROFILE`, and `DOTNET_ROOT` belong to the supported hook contract.
- Add process tests proving parent secrets are absent, required platform values
  remain available, environment-name casing works on Windows, and approved
  hooks can still launch required child tools.

Environment minimization reduces accidental disclosure but does not sandbox an
approved hook, which retains the invoking user's filesystem and process access.

#### SEC-104: No-Follow And Race Confinement

- Add a platform abstraction that traverses and opens entries relative to an
  already-open directory handle instead of reopening validated paths by name.
- On Unix, use directory-relative `openat` operations with no-follow flags and
  `fstat`. On Windows, open with reparse-point controls and inspect stable file
  identity through handle metadata.
- Copy from validated handles, compare type and identity at required boundaries,
  and abort if an entry changes during traversal or copying.
- Add adversarial tests that replace files or directories between validation
  and use. Run them on every supported operating-system family.

Keep native interop isolated and Native AOT compatible. This work is distinct
from SEC-101 because path checks alone cannot close time-of-check/time-of-use
races.

#### SEC-105: OCI Digest Verification

- Capture the registry manifest digest from the image publication step and add
  the full `image@sha256:...` reference to release metadata.
- Pull the published image by digest in release validation, then run help,
  version, exit-code, and mounted-repository smoke tests against that reference.
- Verify labels identify the expected source revision and version. Retain the
  digest with checksums, SBOM, and provenance evidence.
- Document that tags are convenience aliases and digests are immutable image
  identities. Add keyless signature verification later if stronger publisher
  authentication is required.

Digest verification detects tag movement or replacement; it does not establish
that the originally published image was benign.

#### SEC-106: Catalog Publisher Signatures

- Treat signatures as part of hosted-catalog architecture, not as an extension
  to current local and Git source parsing.
- Prefer a reviewed metadata framework such as TUF over a custom signature
  protocol. Define trusted roots, role separation, thresholds, expiry,
  revocation, key rotation, rollback protection, and offline behavior.
- Sign canonical metadata containing pack identity, version, source coordinate,
  digest, size, and metadata version. Verify metadata before resolution and
  pack bytes before use; persist verified provenance in the lock file.
- Prove Native AOT compatibility before selecting a client library. Test
  tampering, expiry, replay, rollback, identity substitution, revocation, and
  trusted root rotation.

## Accepted Residual Risks

- Approved lifecycle scripts run with the invoking user's authority. LunaPack
  does not claim sandboxing.
- A trusted source can publish changed script content in a later pack version.
- LunaPack can restore its own state and managed files but cannot reverse remote
  calls or unrelated effects from an approved process.
- Same-user filesystem races and source-link traversal remain documented under
  ADR-0040 until no-follow snapshot support is implemented.
- No telemetry is collected. User-invoked tools and hooks may independently
  collect or print data outside LunaPack's control.
- Root development tooling reports four high-severity advisories through the
  latest Marp CLI's Puppeteer and `extract-zip` chain. No compatible patched
  release exists; pitch generation consumes maintainer-controlled local input.
- Docusaurus reports 18 high and 6 moderate no-fix advisories in its build-time
  graph. Production serves generated static files, not the Node build process.

## Review Procedure

1. Run locked restore, Release build, unit tests, integration tests, and Native
   AOT publication for each supported runtime.
2. Run dependency, secret, workflow, package, and container scanners. Record
   findings without secret values.
3. Exercise release dry run and inspect every archive, npm package, NuGet
   package, checksum, SBOM, and provenance statement.
4. Run CLI and container smoke tests against disposable repositories.
5. Confirm external trusted-publisher, branch protection, environment, and
   private vulnerability-reporting settings.
6. Update this register; unresolved release gates block publication unless a
   maintainer explicitly accepts and records the residual risk.
