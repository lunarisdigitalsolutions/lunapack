# Publish private packs from GitHub Actions

This example shows a private-pack repository that publishes changed pack
directories through `luna publish`. Copy `lunapack.yml`, `packs/`, `scripts/`,
`tests/`, and the selected workflow files into the root of your private-pack
repository. The workflows in this directory are templates, not workflows run by
the LunaPack repository.

## Set up the repository

1. Replace the example registry URLs and paths in `lunapack.yml`. Keep registry
   credentials out of that file.
2. Set the repository variable `LUNA_VERSION` to an exact Luna release that
   includes OCI publishing. The minimum supported release is `1.6.0`.
3. Keep the pack folder name independent from the pack ID and version in its
   root `pack.yml`.
4. Choose one or more publishing workflows below. Each workflow runs only on
   pushes to `main` that change `packs/**`.

The sample contains `backend` and `security` pack directories. Each has its own
manifest and content; the backend pack also demonstrates a lifecycle script.
`luna publish` validates the complete pack but does not execute its hooks.
Consumers still need to review and authorize scripts before running them.

## Choose registry authentication

### GitHub Container Registry

Copy `.github/workflows/publish-ghcr.yml`. It grants `contents: read` and
`packages: write`, and supplies `github.actor` and `GITHUB_TOKEN` through Luna's
source-bound environment provider. Configure package access so the repository's
`GITHUB_TOKEN` can publish to the selected owner. The `ghcr` source path in
`lunapack.yml` must match that owner and package namespace.

### Azure Container Registry

Copy `.github/workflows/publish-acr.yml`. Configure the `AZURE_CLIENT_ID`,
`AZURE_TENANT_ID`, and `AZURE_SUBSCRIPTION_ID` repository variables. Create a
federated credential for the repository's `main` branch, grant that identity
`AcrPush` on the registry (or the narrowest repository-scoped writer role your
ACR permission mode supports), and set the `acr` source URL and path. The job
uses `azure/login` with GitHub OIDC; it does not store a client secret or run a
Luna login command. `DefaultAzureCredential` uses the Azure CLI identity
provided by `azure/login`.

### Harbor or another OCI Distribution registry

Copy `.github/workflows/publish-generic.yml`. Set the repository variable
`LUNAPACK_OCI_SOURCE` to `harbor` or `generic`, and add `OCI_USERNAME` and
`OCI_PASSWORD` repository secrets for a repository-scoped robot account or
token. The workflow grants only `contents: read`; the registry credentials
provide publish permission. Source-bound credentials work the same way for
Harbor, GHCR, ACR, and compatible registries.

If you copy multiple workflows, each publishes the same changed pack versions
to its configured target. Remove workflows you do not use. Each workflow
serializes its own runs; configure immutable version tags in the registry as
well. Luna rejects an existing version tag, but the OCI Distribution API does
not provide a portable atomic create-if-absent operation across independent
publishers.

## Changed-pack detection

The helper compares `github.event.before` with `github.sha`, uses NUL-delimited
paths, and emits each existing direct child of `packs/` once. It handles
first-push sentinels, multi-file and multi-pack changes, folder names with
spaces, renames, squash and merge commits, and deleted packs. A failed Git
comparison exits nonzero so the workflow cannot mistake missing history for an
empty change set.

Run its fixtures with:

```bash
python3 -m unittest discover -s tests -v
```

The workflows check out full Git history and publish only existing folders that
still contain `pack.yml`. They delegate pack identity, validation, archive
creation, OCI transport, authentication, and immutable-version checks to Luna.
