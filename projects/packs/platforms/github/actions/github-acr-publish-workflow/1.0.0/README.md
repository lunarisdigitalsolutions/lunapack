# GitHub Actions: Azure Container Registry pack publishing

Installs a workflow that publishes changed direct-child directories under
`packs/` with `luna publish --target acr`.

Configure an OCI source named `acr` in `lunapack.yml`. Configure a federated
Azure identity for the repository's `main` branch and grant it the narrowest
registry role that permits publication. Set repository variables
`AZURE_CLIENT_ID`, `AZURE_TENANT_ID`, `AZURE_SUBSCRIPTION_ID`, and
`LUNA_VERSION`. The workflow uses GitHub OIDC with `azure/login`; Luna uses the
resulting Azure CLI identity through `DefaultAzureCredential`.

No permanent Azure client secret or Luna-specific registry credential
environment variables are required. The hosted runner needs Git, Node.js, npm,
and Python 3.
