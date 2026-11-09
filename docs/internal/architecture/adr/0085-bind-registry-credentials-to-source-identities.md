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
- Support native OS storage and source-bound CI credentials.
- Prompt only after an authentication challenge and bound retries per source.
- Keep generic OCI authentication independent of Azure.

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

Noninteractive credentials use `LUNAPACK_OCI_SOURCE`,
`LUNAPACK_OCI_USERNAME`, and `LUNAPACK_OCI_PASSWORD` together. They are scoped
to the selected configured source, used only in the current process, and never
persisted. Optional ACR token exchange uses `DefaultAzureCredential` in a
separate provider; generic OCI operations do not invoke Azure Identity.

## Consequences

- Native store failures produce warnings; there is no plaintext fallback.
- Environment credentials are suitable for CI but remain ambient process
  secrets that users must minimize before authorizing lifecycle hooks.
- ACR supports developer and workload identities without Luna login commands.
- Successful authentication does not prove registry push permission.

## Confirmation

Credential tests cover project/source/endpoint/path isolation, native store
failures, environment matching, secret redaction, bounded prompts, cancellation,
and ACR provider selection.

## More Information

See the `add-oci-registry-sources-and-publishing` OpenSpec change and
`docs/developer/cli/environment.md`.
