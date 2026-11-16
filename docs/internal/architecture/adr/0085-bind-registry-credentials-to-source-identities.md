---
status: accepted
date: 2026-11-08
decision-makers:
  - Lunaris Engineering
---

# ADR-0085: Bind Registry Credentials to Source Identities

## Context and Problem Statement

Registry credentials must support interactive use and unattended publication
without becoming shared project state or being reused for unrelated registries,
projects, or repository paths.

## Decision Drivers

- Keep secrets out of configuration, lock files, logs, and diagnostics.
- Bind native OS credentials to a project and configured source identity.
- Prompt only after an authentication challenge and bound retries per source.
- Keep generic OCI authentication independent of optional Azure identity.

## Considered Options

- Store registry passwords in project or user YAML.
- Use one global credential per hostname.
- Use native credential storage keyed by project and configured source identity.

## Decision Outcome

Chosen option: "Bind native credentials to the canonical project and OCI
source identity," because registry hostnames alone do not distinguish
repository permissions or user intent.

Credential keys include the canonical project directory, configured source
name, normalized registry origin, and repository prefix. Credentials are kept
in the operating-system credential store. Luna attempts anonymous access first,
then retries an authentication challenge with a stored credential and, in an
interactive session, can prompt once and save a validated replacement.

Luna does not select registry credentials from LunaPack-specific environment
variables. Legacy `LUNAPACK_OCI_SOURCE`, `LUNAPACK_OCI_USERNAME`, and
`LUNAPACK_OCI_PASSWORD` variables are ignored without use or disclosure.
Generic OCI and GHCR noninteractive operations require a credential already
available in the native store. Optional ACR token exchange uses
`DefaultAzureCredential` in a separate provider; generic OCI operations do not
invoke Azure Identity.

## Consequences

- Native store failures produce warnings; there is no plaintext fallback.
- Generic OCI/GHCR CI runners must provision native credentials for the exact
  project and source identity before running noninteractive publication.
- ACR supports developer and workload identities without Luna login commands.
- Successful authentication does not prove registry push permission.

## Confirmation

Credential tests cover project/source/endpoint/path isolation, native store
failures, legacy environment-variable non-disclosure, secret redaction, bounded
prompts, cancellation, and ACR provider selection.

## More Information

See [ADR-0084](0084-use-oci-distribution-artifacts-for-pack-releases.md),
the `add-oci-registry-sources-and-publishing` OpenSpec change, and
`docs/developer/cli/environment.md`.
