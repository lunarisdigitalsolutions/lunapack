# Proposal

## Why

Git sources currently accept plaintext `http://` and `git://` transports, which
let network observers read fetched data and alter traffic. Secure transports
should be the default while retaining an explicit, visible exception for
legacy environments.

## What Changes

- **BREAKING**: Block plaintext Git transport before network access by default;
  existing HTTP/Git sources require an explicit preference to continue working.
- Add Boolean preference `git.allow-insecure-transport`, defaulting to `false`,
  configurable in project, user-project, or global scope. The most specific
  configured value wins; an enabled value permits all plaintext Git sources at
  that scope.
- Warn whenever an enabled plaintext source is used and prevent Git credential
  helpers from supplying stored credentials to plaintext requests where
  possible.
- Preserve HTTPS, SSH, scp-style, and local Git source behavior and identity.
- Update schemas, public and internal guidance, the CLI changelog, and focused
  tests.

## Capabilities

### New Capabilities

- `cli-preferences`: Add a scoped insecure-Git-transport preference with a
  default-deny policy.

### Modified Capabilities

- `git-pack-sources`: Require secure transport for Git network operations unless
  the effective preference enables plaintext transport; warn and suppress
  stored credential helpers for the exception.
- `manifest-schemas`: Publish the new preference in project and user-settings
  schemas while retaining version-1 compatibility.

## Impact

Affected systems include CLI preference parsing, scope resolution and
serialization; Git source addition, ref resolution, catalog discovery,
materialization, external pack sources, links, Git process execution; project
and user-settings schemas; tests; `docs/developer`; `docs/internal`; ADR-0101;
the OpenSpec schema specification; and `projects/cli/CHANGELOG.md`.
