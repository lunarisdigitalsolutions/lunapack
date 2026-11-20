---
status: accepted
date: 2026-11-19
decision-makers:
  - Lunaris Engineering
---

# ADR-0101: Require Secure Git Transports by Default

## Context and Problem Statement

Git source operations accepted plaintext `http://` and `git://` URLs. These
transports expose source traffic to network interception or modification. Git
configuration can also rewrite a secure-looking source or follow an HTTP
redirect, bypassing a source-URL-only check.

## Decision Drivers

- Protect Git source confidentiality and integrity by default.
- Preserve HTTPS, SSH, scp-style, and local source behavior and identity.
- Keep legacy plaintext access available through an explicit, visible opt-in.
- Prevent Git configuration from silently bypassing the transport policy.

## Considered Options

- Continue allowing plaintext transports by default.
- Deny plaintext transports with no override.
- Deny plaintext transports by default and provide one scoped Boolean opt-in.

## Decision Outcome

Chosen option: "Deny plaintext transports by default and provide one scoped
Boolean opt-in," because it establishes a secure default while allowing
consumers to make a deliberate compatibility choice.

### Consequences

- Good, because `http://` and `git://` operations fail before network access
  unless `git.allow-insecure-transport` resolves to `true`.
- Good, because project, user-project, and global scopes let teams limit the
  exception without changing source identity or lock ownership.
- Good, because process-level Git protocol restrictions block insecure
  `url.*.insteadOf` rewrites.
- Good, because secure HTTPS operations resolve URL rewrites before enforcing
  certificate verification and redirect rejection on the effective target.
- Good, because `file` is scoped to explicitly local repositories; network
  sources cannot reach UNC or hosted filesystem locations.
- Bad, because secure HTTPS Git operations do not follow redirects, including
  HTTPS-to-HTTPS redirects; consumers must configure the final HTTPS URL.
- Bad, because opted-in HTTP requests can still receive credentials from
  embedded URLs, `http.extraHeader`, or proxy configuration even though Git
  credential helpers are disabled.
- Bad, because repository checkout rejects smudge filters, including Git LFS;
  pack content must be stored as regular Git blobs.

### Confirmation

Confirm the policy with focused preference and Git process tests: default and
scoped resolution, URL rewrite blocking, URL-specific redirect overrides,
certificate-verification overrides, credential-helper non-invocation, warning
sanitization, effective smudge-filter rejection, and unchanged source identity.
The CLI build and unit, integration, and security suites must pass.

## Pros and Cons of the Options

### Continue allowing plaintext by default

- Good, because existing HTTP and Git sources continue to work without changes.
- Bad, because source traffic remains vulnerable to interception and
  modification.

### Deny plaintext with no override

- Good, because every source operation uses a secure transport.
- Bad, because consumers with legacy sources cannot migrate incrementally.

### Deny by default with a scoped Boolean opt-in

- Good, because the default is secure and legacy use remains a deliberate,
  visible exception.
- Bad, because an enabled scope applies to all insecure Git sources within that
  scope rather than one selected source.

## More Information

- [Issue #78](https://github.com/lunarisdigitalsolutions/lunapack/issues/78)
- [Git source behavior](../../../developer/advanced/git-source-behavior.md)
