---
status: accepted
date: 2026-09-01
decision-makers: LunaPack maintainers
---

# ADR-0073: Prefer Stable Tags Over Same-Commit Previews

## Context and Problem Statement

The shared CLI release workflow publishes previews for pushes to `main` and
stable releases for Semantic Version tags. Pushing a main update and its stable
tag together can schedule both channels for the same commit.

## Decision Drivers

- Preserve automatic previews for ordinary CLI changes on `main`.
- Keep stable Semantic Version tags authoritative.
- Avoid publishing a preview and stable package from one commit.
- Retain the artifact-backed pipeline selected by ADR-0068.

## Considered Options

- Publish every scheduled main and tag release.
- Remove automatic previews from `main`.
- Suppress a main preview when its commit already has a stable tag.

## Decision Outcome

Chosen option: "Suppress a main preview when its commit already has a stable
tag," because it preserves both release channels while assigning deterministic
precedence to stable releases.

After checkout, a branch-triggered plan checks tags pointing at `HEAD`. A valid
`v<semantic-version>` tag suppresses matrix creation, builds, and publication for
that branch run. The tag-triggered run remains the stable release owner.

### Consequences

- Combined main and tag pushes publish only the stable channel.
- Ordinary untagged main pushes continue publishing previews.
- The suppressed workflow still records a successful planning job and skipped
  build and release jobs.

### Confirmation

The CLI release workflow contract requires the same-commit tag check, exposes
the `should-release` plan output, and gates both build and release jobs on it.
