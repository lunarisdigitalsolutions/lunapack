---
status: accepted
date: 2026-08-21
decision-makers: LunaPack maintainers
---

# ADR-0026: Name LunaPack's CLI Luna

## Context and Problem Statement

LunaPack is the product name, but its executable and GitHub Release assets used
the longer `lunapack` name. Consumers need a concise command and predictable
download identity without changing LunaPack configuration and lock filenames.

## Decision Drivers

- Keep LunaPack as the product identity.
- Make command entry concise and easy to recognize.
- Keep release assets predictable across supported platforms.

## Considered Options

- Retain `lunapack` as the command and asset prefix.
- Name LunaPack's CLI `luna`.

## Decision Outcome

Chosen option: "Name LunaPack's CLI `luna`", because it separates product
identity from the concise command users run.

The executable is `luna` on Linux and macOS and `luna.exe` on Windows. GitHub
Releases are titled `Luna v<version>` and publish one `luna-cli-<version>-<rid>`
archive for each supported runtime. Product references and project state
filenames remain LunaPack, including `lunapack.yml` and `lunapack-lock.yml`.

### Consequences

- Good, because the command and release assets use one concise identity.
- Good, because product naming and serialized project-state compatibility remain stable.
- Bad, because users of the former command and archive names must update scripts.

### Confirmation

The CLI publish output contains `luna` or `luna.exe`. The build action creates
the specified archive names, and the release action rejects other names.
