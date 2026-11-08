# Tasks

Planning only: all implementation work remains unchecked. Refer to
[design.md](design.md) and the capability deltas in `specs/`.

## 1. OCI Source Contracts and Identity

- [ ] 1.1 Add the OCI source variant to configuration, serialization contexts,
  converters, runtime validators, and project/user identity schemas; verify
  round-trip fixtures and existing local/Git/GitHub-shorthand compatibility.
- [ ] 1.2 Implement endpoint/path/fingerprint normalization through existing
  helpers; verify empty/nested/Windows-style paths, host casing, default and
  nondefault ports, duplicate fingerprints, unsafe paths, credentials in URLs,
  tolerant embedded paths, and explicit-path conflicts.
- [ ] 1.3 Register `luna sources add oci` and help/completion surfaces; verify
  registration succeeds with an unavailable registry and performs zero network,
  identity-provider, credential-store, or prompt calls.
- [ ] 1.4 Extend OCI trust identity and source rename/reference handling; verify
  unchanged source fingerprints, atomic lock/trust reference updates, and no
  silent reuse of credentials under renamed or changed source identities.
- [ ] 1.5 Update developer sources/CLI/schema guidance and internal identity/schema
  documentation; verify examples validate, schema delivery references align,
  and minimum-version/legacy compatibility statements match tests.

## 2. Registry Adapter and Artifact Contract

- [ ] 2.1 Pin ORAS .NET and optional Azure dependency/adapters after reviewing SDK
  capabilities and licenses; verify native pack push/pull support without
  Docker/ORAS CLI and keep SDK types confined to adapters.
- [ ] 2.2 Implement `IOciRegistryClient` domain models and ORAS adapter operations
  for repositories, paginated tags, manifests, blobs, and push; verify focused
  adapter tests for cancellation, timeout, 401/403/404, DNS, TLS, and outage.
- [ ] 2.3 Implement lossless repository/version-tag mapping; verify source paths,
  empty path, pack ID validation, prereleases, build-metadata encoding,
  tag-length limits, and round-trip pack/version identity.
- [ ] 2.4 Generate/validate v1 config, manifest, annotations, artifact/media types,
  descriptor sizes and digests; verify exact output shape, metadata agreement,
  unsupported format/type, missing/multiple ZIP layers, and invalid manifests.
- [ ] 2.5 Document the ORAS boundary, repository mapping and owned artifact format
  in internal/developer authoring guidance; verify examples match serializer
  fixtures and accepted ADRs are created/indexed only when decisions are accepted.

## 3. ZIP Representation and Security

- [ ] 3.1 Create complete directory ZIPs with native .NET compression and no
  wrapper folder; verify byte/path round trips for root manifest, README,
  scripts, nested paths, dotfiles, and rejection of unsafe authoring links.
- [ ] 3.2 Validate download/config/manifest digests and sizes before extraction;
  verify substituted blobs, truncated downloads, descriptor mismatch, corrupt
  ZIP, invalid root manifest, and no mutation after rejection.
- [ ] 3.3 Enforce canonical extraction confinement and documented streaming
  limits; verify absolute/UNC/drive paths, mixed traversal, duplicate/case-folded
  paths, file-directory conflicts, link-like entries, and exact/over-limit
  download, metadata, entry-count, per-file and total expansion tests.
- [ ] 3.4 Give download/extract/publish temporary resources explicit lifetimes;
  verify cleanup on success, failure, cancellation and disposal with no
  persistent OCI cache or writes outside a private temporary root.
- [ ] 3.5 Update threat-model and archive/integrity documentation; verify every
  new trust boundary, safety threshold, unsafe-entry rule, and existing script
  authorization invariant maps to a focused security test.

## 4. Source-Bound Credentials and Automatic Authentication

- [ ] 4.1 Implement `ISourceCredentialStore` and Windows Credential Manager,
  macOS Keychain, Linux Secret Service/keyring adapters; verify get/save/remove,
  native-store failures, per-project/source/endpoint/path isolation, and no
  plaintext or colocated-key fallback.
- [ ] 4.2 Add operation-scoped authentication coordination and hidden prompts;
  verify anonymous success, credential lookup, automatic retry/resume,
  successful persistence, rejected-stored-credential replacement, cancellation,
  bounded retries, and at most one prompt per source including parallel calls.
- [ ] 4.3 Implement source-bound noninteractive environment credentials; verify
  complete/partial/mismatched provider inputs, precedence, token/password use,
  no CI persistence, and no input reads in noninteractive invocations.
- [ ] 4.4 Add redacted authentication/authorization failures and native-store
  persistence warnings; verify authenticated 403 does not prompt repeatedly
  and sentinel secrets never appear in serialized files, fingerprints, logs,
  output, exceptions, diagnostics, telemetry, or redirect/challenge forwarding.
- [ ] 4.5 Document automatic authentication, provider environment contract,
  native-store availability, expiry/replacement and CI recovery; verify no
  login/logout commands are registered or documented as supported.

## 5. Optional Azure Identity

- [ ] 5.1 Implement a separate Azure.Identity/DefaultAzureCredential provider and
  ORAS credential integration with ACR token exchange and repository scope;
  verify generic OCI has no Azure dependency at its source/client boundary.
- [ ] 5.2 Add ACR detection/provider selection with bounded fallback; verify
  username/password works without Azure CLI, unavailable/expired identity
  diagnostics, push authorization failures, and no Azure attempts for GHCR,
  Harbor, or generic endpoints.
- [ ] 5.3 Exercise developer identities, direct workload federation, and the
  azure/login OIDC identity chain; verify native noninteractive ACR read/publish
  without a permanent client secret or Luna login command.
- [ ] 5.4 Document Azure prerequisites, role/scoped permissions, identity-chain
  alternatives, and token exchange architecture; verify examples distinguish
  optional Azure CLI identity from normal registry password authentication.

## 6. User-Local Ignore State

- [ ] 6.1 Extend existing per-project user settings with ignored source identities
  and `ignore`/`unignore` handlers; verify atomic/idempotent writes, unknown-name
  rejection, canonical-project isolation, and preservation of unrelated trust,
  acknowledgements and denial settings.
- [ ] 6.2 Display `ignored locally` in configured-order source listings and
  inspection without source access; verify local, Git/GitHub-shorthand and OCI
  sources remain visible and rename preserves matching ignore state.
- [ ] 6.3 Gate catalog, direct resolution, links, external content, authentication,
  and publishing before source access; verify ignored sources receive zero
  filesystem-source, Git, registry, credential-provider, or prompt calls.
- [ ] 6.4 Report required/locked ignored-source conflicts with unignore guidance;
  verify no silent source substitution, no project/lock changes, and local-only
  audit/safe uninstall remain usable when their evidence suffices.
- [ ] 6.5 Document per-user/project scope and source identity semantics in
  developer/internal guidance; verify ignore state never appears in shared
  configuration/lock/schema contracts or a committed repo-local file.

## 7. Catalog Integration and Source Failure Isolation

- [ ] 7.1 Extend source discovery outcomes with candidates and typed source
  failures/limitations; verify mixed-source partial results, preserved precedence,
  all-active-source failure exit status, intentionally all-ignored reporting,
  and explicit incomplete-resolution diagnostics.
- [ ] 7.2 Integrate OCI repository/version enumeration and normal search/inspect
  output; verify pagination, path boundaries, SemVer ordering/limits, metadata
  relevance, invalid candidate diagnostics, and current-state queries.
- [ ] 7.3 Add direct repository resolution for explicit/locked/composite IDs;
  verify readable repositories still install with global catalog permission
  denied and discovery does not claim complete or falsely empty results.
- [ ] 7.4 Classify Git authentication and availability failures using existing
  Git credentials; verify credential-manager/SSH and local-ignore guidance,
  no Luna Git-password storage, optional-source continuation, and required-source
  failure without mutation.
- [ ] 7.5 Document source failure isolation and ACR/GHCR/Harbor discovery limits in
  developer troubleshooting/source guidance and internal catalog architecture;
  verify documented warnings and exit behavior match mixed-source fixtures.

## 8. Lifecycle, Digest Locks and Offline Behavior

- [ ] 8.1 Materialize OCI directories into normal install/inspect/dry-run planning
  and dependency resolution; verify composite local/Git/OCI graphs, conditional
  references, parameters/templates/remapping, ownership, and existing script
  trust/denial behavior without an OCI-specific install engine.
- [ ] 8.2 Add OCI source/repository/version/manifest/content digest lock provenance
  with source identity and distributed lock schema updates; verify legacy
  local/Git locks, transitive provenance, serialization redaction, atomic
  persistence and rollback after required-source/integrity/state-write failures.
- [ ] 8.3 Retrieve locked releases by digest and detect moved-tag violations;
  verify missing digests fail without source switching, one operation uses one
  selected digest, and online checks distinguish mutation from a new version.
- [ ] 8.4 Integrate outdated/update/removal and offline reporting; verify
  second-version updates, normal source-switch rules, local audit/uninstall,
  offline unchecked OCI state, no remote/cache access offline, and no false
  "up to date" result for unchecked packs.
- [ ] 8.5 Reject unsupported OCI arbitrary-file link/external-source declarations
  with guidance while retaining approved local/Git external content for OCI
  packs; verify no implicit broadening of pack-local external-source contracts.
- [ ] 8.6 Update install/update/inspect/audit/lock/offline documentation and
  internal lifecycle/trust guidance; verify examples and ownership/provenance
  explanations match tested lifecycle output and rollback behavior.

## 9. Pack Publishing

- [ ] 9.1 Add `luna publish <folder> --target <source>`, publisher selection and
  help/completion; verify required/unknown/ignored/unsupported targets and no
  accepted `--type`, `--id`, or `--version` override.
- [ ] 9.2 Load ID/version only from root `pack.yml` and validate complete content
  with existing authoring validation; verify folder/ID mismatch is supported,
  invalid manifests/content fail before upload, and no hooks or project-state
  mutation occurs during publish.
- [ ] 9.3 Publish one ZIP/config/manifest through the shared registry and auth
  boundaries; verify interactive authentication resumes and CI never prompts,
  read-only credentials do not prove push permission, and success includes the
  verified reference/digest rather than partial-upload success.
- [ ] 9.4 Reject existing version tags without overwrite, verify completion
  digest and document registry-side concurrency protection; test sequential
  duplicate/identical/wrong-artifact rejection separately from concurrent
  publish races and supported immutable-tag policies.
- [ ] 9.5 Document publisher architecture, immutable-version authoring and
  failures in developer/internal guidance; verify examples require only folder
  and source target and temporary files are cleaned on every publish exit.

## 10. Private-Pack Repository and GitHub Actions Sample

- [ ] 10.1 Deliver a private-pack repository example with valid source map,
  root pack manifests and content folders; verify its configuration/packs
  validate and generic/ACR/GHCR/Harbor paths contain no committed credentials.
- [ ] 10.2 Deliver the planned workflow with documented pinned Luna installation,
  `main`/`packs/**` trigger, before/after comparison, NUL-delimited deduplication
  and deleted-folder skipping; verify multi-file, multi-pack, spaces, first
  push, rename, squash, merge, deletion and failed-diff fixtures.
- [ ] 10.3 Add OIDC/ACR and source-bound GHCR/generic credential variants with
  minimum permissions; verify no permanent Azure client secret, Luna login,
  direct packaging/ORAS/API logic, or pack-ID/version lookup in the workflow.
- [ ] 10.4 Exercise unchanged-version and authentication failures through
  `luna publish`; verify jobs fail visibly, and document registry immutability
  setup, identity roles, CLI minimum version, action pins and serialization.
- [ ] 10.5 Update developer automation/github-actions navigation and product
  delivery/acceptance documentation; verify examples are published as available
  only with the shipped feature and links remain valid.

## 11. Cross-Surface Integration and Regression

- [ ] 11.1 Run real generic OCI Distribution registry CI: create, publish,
  discover, list versions, inspect manifest, download, install, publish second
  version, detect/update, reject duplicate/wrong-type/corrupt content; verify
  exact reference/digests and no persistent OCI cache.
- [ ] 11.2 Exercise one registry with different paths and credentials, catalog
  denial with direct access, optional/required failures and ignored sources;
  verify repository scope never leaks credentials or causes substitution.
- [ ] 11.3 Verify GHCR and ACR compatibility with controlled integration
  identities and Harbor smoke tests where practical; deliver a documented
  compatibility matrix and explicit infrastructure-dependent limitations.
- [ ] 11.4 Run existing local/Git/GitHub discovery, install/dry-run, dependency,
  update, lock, audit, trust/denial, source precedence, removal, rename and link
  regressions plus native credential-store tests across supported OSes.
- [ ] 11.5 Run existing build/type checks, selected unit/security/integration
  suites, schema validation, documentation/link checks and package delivery
  validation; verify no unavailable feature is documented and add only shipped
  externally observable CLI changes to `projects/cli/CHANGELOG.md`.
