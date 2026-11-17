# GitHub Actions: GHCR pack publishing

Installs a workflow that publishes changed direct-child directories under
`packs/` with `luna publish --target ghcr`.

Configure an OCI source named `ghcr` in `lunapack.yml`. The workflow runs on a
persistent Linux self-hosted runner. Its user must have a working native
credential store with a credential authorized to publish to the configured
GHCR package. Seed credentials with an interactive Luna operation on that
runner before enabling the workflow. The workflow does not use `GITHUB_TOKEN`,
Luna-specific credential environment variables, or credential files.

Set repository variable `LUNA_VERSION` to an OCI-capable Luna release. The
runner also needs Git, Node.js, npm, Python 3, and an available native
credential store. Credential reuse is bound to the same project path, source
name, registry, and repository path.
