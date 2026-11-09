# Publish private packs from GitHub Actions

Publish changed pack directories from a private repository to GHCR, Azure
Container Registry (ACR), Harbor, or another OCI Distribution registry. The
maintained [private-pack example](https://github.com/lunarisdigitalsolutions/lunapack/tree/main/examples/oci-private-packs)
includes manifests, changed-pack detection, tests, and three workflow templates.

## Prerequisites

- Use Luna **1.6.0 or later**, pinned to an exact release in the repository
  variable `LUNA_VERSION`.
- Add an OCI source to the repository's `lunapack.yml`; keep passwords and
  tokens in GitHub secrets, not project configuration.
- Configure the registry to reject overwrites of published version tags.
- Protect `main` and review pack changes before they can use registry write
  permissions.

## Choose registry authentication

Copy the matching workflow from the example repository and replace its example
registry URL and repository path.

| Registry                       | Workflow identity                                                                                                | Minimum workflow permissions        |
| ------------------------------ | ---------------------------------------------------------------------------------------------------------------- | ----------------------------------- |
| GHCR                           | `github.actor` and `GITHUB_TOKEN`, scoped to the configured source                                               | `contents: read`, `packages: write` |
| ACR                            | GitHub OIDC through `azure/login`; grant the federated identity `AcrPush` or the narrowest supported writer role | `contents: read`, `id-token: write` |
| Harbor or another OCI registry | Repository-scoped robot account or token through source-bound environment variables                              | `contents: read`                    |

The ACR workflow needs repository variables `AZURE_CLIENT_ID`,
`AZURE_TENANT_ID`, and `AZURE_SUBSCRIPTION_ID`, plus a federated credential
restricted to the repository's `main` branch. It does not need a permanent
Azure client secret or a Luna login command. The generic workflow reads
`LUNAPACK_OCI_SOURCE` from a repository variable and `OCI_USERNAME` and
`OCI_PASSWORD` from repository secrets.

Luna validates the pack before publishing and does not execute its lifecycle
hooks. Consumers still need to review and authorize pack scripts before
installing them.

## Publish changed directories

The example workflows run on pushes to `main` that change `packs/**`. They
check out full Git history, compare the event's before and after commits, and
pass NUL-delimited paths to `luna publish`. This preserves names containing
spaces and avoids treating deleted directories as publishable packs. Failed
Git comparisons stop the workflow instead of silently publishing nothing.

Each existing pack directory is published with:

```bash
luna publish packs/backend --target ghcr
```

`--target` must name a configured OCI source. Luna reads the pack ID and version
from the root `pack.yml`; it rejects invalid content and existing version tags.
The successful output includes the published reference and verified manifest
digest. Bump the pack version before publishing changed content. Registry-side
immutable tags are still required to prevent later movement or races between
independent publishers.

The sample workflows serialize runs per registry target. If one fails,
GitHub Actions reports the failure; fix the source, credentials, or pack
version and rerun after checking whether the registry already received the
release.

For source configuration, registry discovery limits, and credential behavior,
see [Add a pack source](../sources.md#add-an-oci-registry-source) and the
[environment reference](../cli/environment.md#oci-registry-credentials).
See the [command reference](../cli/commands.md#pack-publishing) for the full
`luna publish` contract.
