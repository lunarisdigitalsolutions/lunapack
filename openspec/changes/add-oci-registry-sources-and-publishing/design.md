# Design

## Context

See [proposal.md](proposal.md) for motivation and affected surfaces. This
document records the design implemented by this change; public usage guidance
lives under `docs/developer`.

Current source configuration uses an ordered YAML sequence of named entries
represented internally by `ProjectConfiguration.Source` variants and custom
serialization. `github` is CLI shorthand for a persisted `GitSource`, not a
third runtime source class.
`PackCatalog.BrowseAsync` dispatches local/Git discovery and returns on the first
failed source. `CatalogService`, graph resolution, source selection, materializers,
and lifecycle operations currently consume these results.

Source fingerprints are separate from configured names, reject equivalent
duplicates, and participate in trust/provenance. Existing source rename updates
workspace references atomically. Existing user settings live outside the
repository at `~/.lunapack/config.yml`, keyed by canonical project directory,
with trust, acknowledgement, and denial state. Reuse this store for ignoring;
it is not a credential store.

## Goals / Non-Goals

**Goals:**

- Extend discovery/materialization and source identity boundaries without
  spreading registry transport or native credential-store APIs into domain,
  graph resolution, or trust.
- Preserve existing transactional lifecycle, ownership, external-content
  approval, hook authorization, semantic-version, and source-switch rules.
- Make credentials, source availability, and ignore policy consistent across
  catalog and lifecycle paths, including direct known-repository resolution.
- Specify artifact, normalization, safety limits, CI credentials, and failure
  semantics sufficiently to support implementation and observable tests.

**Non-Goals:**

- No hosted catalog, global discovery guarantee, or registry-side publisher
  identity is introduced by this change.
- No Docker/ORAS subprocesses, persistent OCI cache, explicit authentication
  sessions, Git-password storage, or registry-specific install engine.
- No new pack-ID/version CLI overrides, forced publication overwrite, automatic
  trust grants, registry deletion, or OCI-based arbitrary-file Luna Links.
- No global catalog guarantee for restricted registries; no client-only atomic
  tag creation claim that the OCI Distribution API cannot enforce.

## Decisions

### 1. Extend existing source dispatch and materialization

Introduce an OCI source model alongside local/Git and integrate a narrowly
defined source-provider boundary where catalog and materialization currently
dispatch by source type. Use an `OciSource` implementation and
`IOciRegistryClient` with domain-owned repository, version, descriptor, and
failure models. The direct .NET HTTP adapter implements repository/tag queries,
manifest/blob retrieval, and push operations using the required OCI Distribution
endpoints. Metadata reads are bounded; pagination, bearer realms, and upload
locations are validated before they are followed. Bearer realms must use the
registry's HTTPS origin so registry credentials are never forwarded to an
untrusted token service.

The adapter accepts credentials, cancellation, timeouts, and destination
streams; it never prompts, selects project state, executes hooks, or logs
secrets. No transport or platform credential-store types cross the adapter
boundary. The publisher uses the same client. Optional Azure credentials live
in a separate adapter/provider, not in the generic OCI source or client
interface.

Use direct .NET HTTP and platform-native credential APIs, not subprocess calls
or reflection-dependent OCI and credential-cache packages. The implementation
uses `Azure.Containers.ContainerRegistry` 1.4.0 (MIT) and optional
`Azure.Identity` 1.21.0 (MIT) only for ACR token exchange. OCI transport and
credential storage remain AOT-compatible without ORAS or
`ktsu.CredentialCache`. Bicep is architectural inspiration only: folder -> CLI
-> owned artifact -> registry -> CLI -> normal pipeline.

### 2. Keep endpoint, repository path, and credentials distinct

Persist a normalized HTTPS registry origin in `url`; lowercase DNS host, remove
the default HTTPS port/trailing slash, retain nondefault ports, and reject
userinfo, query strings, fragments, or unsupported schemes. HTTPS is the
production default. Local-registry integration tests use a trusted local TLS
fixture rather than relaxing production TLS verification.

Normalize path separators through the existing path helper, then apply
repository semantics: collapse redundant separators and `.` segments, reject
rooted/drive paths and `..`, validate OCI repository components, and omit an
empty path. Do not lowercase a repository path to make it valid: reject invalid
case instead of selecting another repository. The path is not a filesystem URL.

For tolerant URL input, split a safe embedded repository path into `path` when
no explicit path exists. If both are present, allow only canonical equality;
report conflicts. Fingerprints are endpoint plus canonical path, with an OCI
type discriminator. Different paths on one registry are independent;
equivalent endpoint/path declarations remain duplicates regardless of name.

Repository references append the manifest's exact pack ID. Reject IDs that
cannot be represented safely and losslessly. Valid SemVer build metadata uses
`+`, which tags cannot contain; encode `+` as `_` for version tags and decode
only after validating the resulting SemVer. SemVer itself cannot contain `_`,
making this reversible. Preserve the exact manifest version in metadata and
lock state. Enforce OCI tag syntax/length; never replace pack IDs or silently
truncate versions. Test prerelease/build metadata and invalid mappings.

### 3. Use one explicit v1 artifact contract

Use an OCI image manifest envelope with:

- Manifest `mediaType`: `application/vnd.oci.image.manifest.v1+json`.
- `artifactType`: `application/vnd.lunapack.pack.v1`.
- Config `mediaType`: `application/vnd.lunapack.pack.config.v1+json`.
- Config JSON: required `id`, exact `version`, and integer `formatVersion: 1`.
- Exactly one layer:
  `application/vnd.lunapack.pack.content.v1+zip`, with SHA-256 digest and size.
- Annotations: `io.lunapack.pack.id`, `io.lunapack.pack.format-version`,
  `org.opencontainers.image.version`, and optional standard title, description,
  source, and revision fields when available.

The ZIP has `pack.yml` at root, not a wrapper directory. It contains the complete
validated authoring tree, including README, scripts, nested paths, and dotfiles,
using `System.IO.Compression`/`ZipFile`. No layer-per-file representation.
Metadata supports discovery/inspection hints but cannot override actual pack
behavior: fetch and validate `pack.yml` and require agreement with config,
repository-derived ID, requested version, and annotations when present.

Validate manifest/config/content descriptors and digests before use, including
format version, single-layer structure, size, and supported media types.
Unrelated OCI artifacts are not candidates. Malformed LunaPack artifacts produce
diagnostics and cannot enter installation. Publishing validates through existing
pack authoring rules, without running hooks or installing content.

### 4. Treat discovery as limited, direct resolution as first-class

Attempt supported repository enumeration and honor pagination. Filter by exact
path-segment boundary, then validate version tags and artifacts using normal
catalog selection. Do not assume `/v2/_catalog` is present or authorized in ACR,
GHCR, Harbor, or repository-scoped deployments.

Catalog results carry successful candidates and typed per-source failures or
limitations, never a success-shaped empty fallback for an access error.
Known IDs from explicit requests, existing locks, or composite references map
directly to `{path}/{id}`; query their tags/manifests without catalog access.
Discovery limitations remain visible even when a known pack can be installed.
No new mandatory global index/repository service is introduced.

An operation-scoped result can retain fetched metadata to avoid duplicate
requests during that invocation. Do not persist it as an OCI cache. Inspection
and planning can materialize content temporarily, then reuse existing validators
and display logic. Offline checks use lock/local evidence only and explicitly
report that current OCI state was not checked.

### 5. Coordinate bounded demand-driven authentication

An application-level source authentication coordinator owns prompting/retry.
Start source access with the operation's available context, including anonymous
access when no credential is selected. For a genuine authentication challenge:

1. Resolve a source-bound explicit noninteractive credential, stored native
   credential, or suitable optional Azure identity.
2. Retry with the selected usable credential; mark rejected providers/credentials
   as rejected for this invocation.
3. If interactive and still unauthenticated, prompt once for that source with
   registry/path, username (from configuration when present), and a hidden secret.
4. Validate, securely save successful interactive credentials, and resume the
   original read/publish operation.
5. If noninteractive, cancelled, denied, or the replacement is rejected, return
   a typed source failure. Do not prompt indefinitely or retry outages as auth.

Use operation-scoped coordination so parallel requests for one source do not
create multiple prompts. A valid authenticated `403` is authorization failure;
an anonymous auth-related denial requires challenge-aware classification.
Read validation does not prove push permission. Save only validated credentials;
publish success requires manifest completion, not merely successful login.

Native storage is abstracted as `ISourceCredentialStore` with get/save/remove,
credential key/value, cancellation, and explicit failures. Use Windows
Credential Manager, macOS Keychain, and Linux Secret Service/compatible keyring.
No plaintext fallback. If the native service fails, explicitly warn that
persistence failed and allow only already-validated in-memory credentials for
the current invocation; never promise future reuse. CI provider credentials
are not automatically persisted.

Key credential entries by a stable, collision-resistant representation of
canonical project, configured source name, normalized OCI endpoint and path,
with a LunaPack/type namespace. Nonsecret metadata can retain username,
registry/path, and key version. Never share secrets solely by hostname. A rename
creates a new credential identity: resolve/prompt on later access rather than
silently transferring secrets. Old credentials cannot match the new source.

### 6. Provide explicit CI credentials and separate ACR identity

Document a minimal environment provider:

```text
LUNAPACK_OCI_SOURCE=<exact configured source name>
LUNAPACK_OCI_USERNAME=<registry username>
LUNAPACK_OCI_PASSWORD=<password or token>
```

The source selector is mandatory when either credential variable is supplied.
Partial/mismatched input fails clearly without printing values. It binds one
source per invocation, preventing hostname-based reuse; separate invocations
can publish different sources with different secrets. This provider has
precedence over stored credentials. Secrets exist only in process memory.

Optional Azure provider uses `Azure.Identity`/`DefaultAzureCredential` behind
the `IOciAccessTokenProvider` boundary. Select it only for detected ACR
endpoints or explicit source-bound identity-provider configuration; do not try
Azure for GHCR/Harbor/generic hosts. Acquire an Azure token and perform the
required ACR token exchange/scoping through the credential adapter; an Azure
management token alone is not a registry password. Never persist acquired
access/refresh tokens in project/user settings.

Support available developer identity chains and workload federation. The sample
below uses `azure/login` with GitHub OIDC; the resulting Azure CLI identity can
be consumed by `DefaultAzureCredential`. Azure CLI is an optional identity-chain
component in that example, never required for ordinary username/password use.
Document direct workload-identity environment requirements for environments
that supply a federated token without Azure CLI.

### 7. Reuse project/user settings for ignore policy

Add `ignoredSources` configured-source identities to the existing per-project
user-settings record in `~/.lunapack/config.yml`. Identity, rather than name-only
storage, preserves ignore state across rename and prevents inheritance after an
endpoint/path change. Matching is restricted to the canonical current project.
No global ignore scope or new repo-local file is needed.

Commands validate names, resolve current identities, and atomically save through
`UserSettingsStore`, preserving trust/acknowledgement/denial fields. Listing
projects identities back to current names and shows `ignored locally`.
Repeated operations are idempotent. Removed/different source identities do not
silently match stale state. Ignoring is never source removal or trust revocation.

Filter ignored sources before all discovery, direct resolution, link/external
access, authentication, and target publishing. If locked/explicit resolution
requires one, report the pack/content and `luna sources unignore <name>`.
Local-only audit/uninstall remains possible when existing lock evidence suffices.

### 8. Make source availability explicit without weakening transactions

Return candidates with per-source typed outcomes. Keep configured source order,
even when failures/ignored entries are removed from candidate participation.
Accessible sources remain usable with warnings for optional failed sources.
If all active sources fail, browsing returns nonzero; if all are deliberately
ignored, report no active sources without pretending remote access occurred.

Resolution must know which failures affect a locked/explicit source or required
graph member. A missing pack after a partial search is unresolved from accessible
sources, not definitively absent. Do not silently switch a locked source.
Whole-graph lifecycle transactions remain unchanged: failure of any required
member prevents that requested install/update from partially mutating state.

Apply the same failure isolation to Git/local where meaningful. Git credentials
remain external: credential helpers, Git Credential Manager, SSH configuration
and agent. Classify authentication/permission/network/timeouts from controlled
process results and redact diagnostics; never ask for Git passwords.

### 9. Publish through a source-selected publisher boundary

Add `IPackPublisher` capability selection and an OCI publisher. The command owns
folder/target validation and normal CLI output; publisher validates the root
manifest, derives ID/version, builds the artifact, and delegates transport.
Local/Git publication is not implemented; unsupported targets fail before work.
The same authentication coordinator handles read and push authorization.

Check the exact version tag before uploading; treat any existing tag as a
duplicate, even for identical bytes or unrelated artifact types. Publish blobs,
then manifest/tag, and verify the resulting manifest digest before success.
On failure clean local temporary files; do not delete remotely shared blobs.
No overwrite flag. Reproducibility can improve ZIP ordering/timestamps, but does
not make duplicate-version publication acceptable.

OCI Distribution has no universal atomic create-if-absent tag operation.
Preflight alone cannot prevent a competing publisher from racing. Serialize the
sample job and document registry-side immutable-tag/repository policies as
required for strict protection across independent publishers. Use conditional
creation where a registry supports it, never claim generic atomic guarantees,
and detect/report a completion digest mismatch. Validate duplicate rejection
and race behavior separately; sequential duplicate rejection is not a concurrency
test. Digest locking protects consumers if a registry tag is changed externally.

### 10. Apply explicit archive and transport trust boundaries

Treat all registry responses/content as untrusted. Set bounded cancellation,
timeouts, pagination, download/metadata limits, and ZIP limits. Proposed initial
safety ceilings are 512 MiB ZIP download, 1 MiB config/manifest metadata each,
100,000 ZIP entries, 512 MiB expanded per file, and 2 GiB total expanded bytes.
Enforce exact thresholds while streaming, not only attacker-declared sizes; test
exactly-at-limit and over-limit behavior. Document these ceilings when
implemented.

Before extraction, reject rooted/drive/UNC paths, mixed-separator traversal,
duplicate normalized destinations, case-folding collisions on relevant systems,
file/directory conflicts, symbolic-link/reparse-like entries, and paths escaping
the private extraction root. Reject unsafe authoring links during ZIP creation
rather than following them outside the selected folder. Preserve supported
regular-file content; executable permission metadata cannot grant trust.

Use random user-private temporary directories with explicit lifetimes and
cleanup on every exit. Validate digest/size before trusting/extracting content,
then validate the pack before any lifecycle application. Preserve existing
script authorization, including explicit denial, and atomic ownership rules.

Reject credential-bearing source URLs and redact transport/process failures
before they enter result/log/exception paths. Do not dump raw provider objects,
HTTP authorization, registry challenge secrets, or URLs with tokens. Enforce TLS
verification; never forward static credentials blindly across redirects or
unrelated hosts. Validate challenge endpoints and scope credential transmission
to intended registry/token exchanges. Bound identity/provider attempts.

### 11. Keep schemas, docs, and delivery aligned at implementation time

Extend project source variants, lock provenance, and user-setting identities
additively under existing schema contracts; preserve valid local/Git projects
and settings. OCI records require source identity and manifest/content digests;
legacy records remain unchanged. Update JSON/YAML contexts, converters,
runtime validators, trust matching, fingerprinting, CLI help/completion, and
distributed schema copies/references together.

OCI is a pack-distribution source, not arbitrary-file link transport in v1.
Explicit OCI-backed link/external-file source declarations fail with actionable
unsupported-source guidance; OCI packs can still use existing approved local/Git
external-content references. Do not broaden pack-local external-source schemas
implicitly.

Public docs explain the shipped commands and limits. Internal docs record
architecture, source-required failure handling, credential resolution, artifact
format and threat model. Product docs record the delivery milestone. Accepted
decisions are recorded in ADRs 0084-0089 and indexed.

## Risks / Trade-offs

- Catalog APIs missing/denied -> limited-discovery warnings plus direct
  known/locked repository queries; document per-registry limitations.
- Concurrent publication races -> immutable-tag registry policies, job
  serialization, supported conditional operations and digest verification;
  no false universal client-only immutability claim.
- Headless Linux without Secret Service -> explicit persistence-unavailable
  diagnostics and source-bound CI providers, never plaintext fallback.
- Azure SDK chain latency or wrong identity -> source-scoped provider selection,
  bounded attempts, actionable failure, and generic credential fallback.
- Secret-bearing transport errors -> typed redacted failures and sentinel-secret
  tests for logs, serialization, diagnostics, exceptions and telemetry.
- ZIP bombs/path collisions -> streaming thresholds, preflight plus canonical
  destination checks, cross-platform security tests, private temp cleanup.
- Current pack IDs/versions outside OCI syntax -> explicit lossless mapping
  validation; no project-wide compatibility change for local/Git packs.
- No persistent OCI cache -> repeated online transfers and no offline restore;
  retain only operation-scoped metadata and normal lock evidence.
- Source failure isolation changing all-or-nothing catalog behavior -> explicit
  partial outcomes, source-order regression tests, unchanged whole-graph mutation.
- Source rename changes credential binding -> later demand-driven authentication;
  ignore/trust/lock identity remains stable, and no secret migration is implicit.

## Migration Plan

1. This change implements OCI sources, credentials, local ignore state,
   lifecycle integration, publishing, schemas, documentation, and the
   private-pack example.
2. Track implementation and verification status in `tasks.md`; focused unit,
   security, integration, and workflow-fixture tests cover the shipped behavior.
3. Preserve existing schema-version-compatible local/Git configurations and user
   settings. Add OCI provenance only for successfully resolved OCI records.
4. Verify the generic TLS registry integration. Live GHCR/ACR identities and
   Harbor smoke coverage require controlled external registry infrastructure;
   document this limitation rather than claim provider validation.
5. Ship documentation, threat model, supported CLI version, and sample together.
   Older CLIs cannot read new OCI declarations; document minimum version rather
   than reinterpreting them as Git.
6. Rollback to an older CLI requires removing new OCI declarations/consumers
   through safe lifecycle commands or restoring prior backed-up project state;
   never strip provenance from live ownership records. Native credentials and
   user-local ignore state remain outside project files.

## Private-Pack Example

The maintained example lives in `examples/oci-private-packs`. Its workflows
are consumer templates, not workflows run by the LunaPack repository.

```text
lunapack.yml
packs/
  dotnet-project/pack.yml
  dotnet-project/files/
  dotnet-project/scripts/
  backend/pack.yml
  security/pack.yml
```

```yaml
schemaVersion: 1
sources:
  - name: private
    type: oci
    url: https://lunapack.azurecr.io
    path: packs
packs: []
```

Pin a shipped OCI-capable Luna version in repository variable `LUNA_VERSION`.
Configure a federated Azure identity restricted to repository push permissions.
The sample uses the already documented npm installation method. CI-only
`azure/login` supplies an OIDC-backed Azure identity; this is not a Luna login.

```yaml
name: Publish changed LunaPacks

on:
  push:
    branches: [main]
    paths: ['packs/**']

permissions:
  contents: read
  id-token: write

concurrency:
  group: publish-private-lunapacks
  cancel-in-progress: false

jobs:
  publish:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
        with:
          fetch-depth: 0

      - name: Install Luna
        shell: bash
        env:
          LUNA_VERSION: ${{ vars.LUNA_VERSION }}
        run: |
          set -euo pipefail
          : "${LUNA_VERSION:?Set LUNA_VERSION to an OCI-capable release}"
          npm install --global "@lunarisdigitalsolutions/lunapack@$LUNA_VERSION"
          luna --help

      - name: Determine changed packs
        shell: bash
        env:
          BEFORE: ${{ github.event.before }}
          AFTER: ${{ github.sha }}
        run: |
          set -euo pipefail
          base="$BEFORE"
          if [[ -z "$base" || "$base" =~ ^0+$ ]]; then
            base="$(git hash-object -t tree /dev/null)"
          elif ! git cat-file -e "$base^{commit}"; then
            git fetch --no-tags origin "$base"
          fi
          git diff --no-renames --name-only -z "$base" "$AFTER" -- packs/ \
            > "$RUNNER_TEMP/changed-files.bin"
          while IFS= read -r -d '' file; do
            case "$file" in
              packs/*/*)
                relative="${file#packs/}"
                pack="${relative%%/*}"
                printf '%s\0' "$pack"
                ;;
            esac
          done < "$RUNNER_TEMP/changed-files.bin" \
            | sort -zu > "$RUNNER_TEMP/changed-packs.bin"

      - name: Azure identity through OIDC
        uses: azure/login@v2
        with:
          client-id: ${{ vars.AZURE_CLIENT_ID }}
          tenant-id: ${{ vars.AZURE_TENANT_ID }}
          subscription-id: ${{ vars.AZURE_SUBSCRIPTION_ID }}

      - name: Publish changed packs
        shell: bash
        run: |
          set -euo pipefail
          while IFS= read -r -d '' pack; do
            if [[ -d "packs/$pack" ]]; then
              luna publish "packs/$pack" --target private
            fi
          done < "$RUNNER_TEMP/changed-packs.bin"
```

The workflow contains no artifact logic and no ID/version lookup. NUL-delimited
paths preserve whitespace, `--no-renames` exposes both sides of renamed content,
and each direct child is published once. Deleted directories are skipped;
unchanged versions fail via Luna. Before implementation, align action versions
and SHA pinning with repository conventions and verify first-push, merge,
squash, deletion, rename, and failed comparison fixtures.

For GHCR, bind `LUNAPACK_OCI_SOURCE=private`,
`LUNAPACK_OCI_USERNAME=${{ github.actor }}`, and
`LUNAPACK_OCI_PASSWORD=${{ secrets.GITHUB_TOKEN }}` to the publish step, grant
`packages: write`, and configure the appropriate owner path and package access.
Harbor/generic examples use the same source-bound environment contract with a
repository-scoped robot account/token. Do not echo these environment variables.
