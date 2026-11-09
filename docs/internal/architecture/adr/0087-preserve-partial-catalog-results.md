---
status: accepted
date: 2026-11-08
decision-makers:
  - Lunaris Engineering
---

# ADR-0087: Preserve Partial Catalog Results

## Context and Problem Statement

Configured sources can fail independently because of authentication,
authorization, network outages, or provider-specific catalog restrictions.
Failing the entire catalog discards readable results; treating a failed source
as empty falsely claims completeness.

## Decision Drivers

- Preserve candidates from reachable sources and configured-source precedence.
- Report source-specific failure or incompleteness without success-shaped
  fallbacks.
- Fail when every active source is unavailable.
- Allow direct resolution when the pack ID and repository are already known.

## Considered Options

- Fail discovery on the first source error.
- Convert every source error into an empty successful result.
- Return candidates and typed per-source failures together.

## Decision Outcome

Chosen option: "Return partial candidates with typed per-source failures,"
because availability of one configured source should not hide results from
another, and failures must remain observable.

Discovery continues after optional-source failures and retains successful
candidates in configured order. If all active sources fail, the operation
returns a nonzero result. Intentionally ignoring every source is reported
separately from source failure. Direct requests and locked releases query known
repositories and do not claim that global discovery was complete.

## Consequences

- Search and discovery can show useful partial results with warnings.
- Consumers can distinguish unavailable sources from empty catalogs.
- A successful direct lookup does not imply complete registry enumeration.
- Failure diagnostics become part of the public CLI behavior and require
  focused tests.

## Confirmation

Catalog tests cover mixed reachable/unavailable sources, all-source failure,
all-ignored state, direct known-repository resolution, and configured
precedence. Integration tests verify nonzero exit status and source-scoped
warnings.

## More Information

See the `add-oci-registry-sources-and-publishing` OpenSpec change and
`docs/developer/sources.md`.
