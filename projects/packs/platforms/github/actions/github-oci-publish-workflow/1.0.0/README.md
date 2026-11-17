# GitHub Actions: Generic OCI pack publishing

Installs a workflow that publishes changed direct-child directories under
`packs/` with `luna publish --target generic`.

Configure an OCI source named `generic` in `lunapack.yml`. The workflow runs on a
persistent Linux self-hosted runner. Its user must have a working native
credential store with credentials for the exact project, source name, registry,
and path. Seed credentials with an interactive Luna operation on that runner
before enabling the workflow; Luna has no registry login command or
credential-environment-variable provider.

Set repository variable `LUNA_VERSION` to an OCI-capable Luna release. The
runner also needs Git, Node.js, npm, Python 3, and an available native
credential store.
