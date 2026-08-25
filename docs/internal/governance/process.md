# Maintainer Process

Review changes for correctness, schema compatibility, lifecycle impact, source
provenance, documentation, and policy compliance. Validate the narrowest
affected build and tests before wider checks.

Pack releases require a reviewed manifest, rendered-content verification,
semantic version, changelog entry, owner, and immutable local or Git source
content. Update behavior must preserve state when planning or writes fail.

CLI releases require a reviewed changelog, a versioned release tag, successful
build and test automation, and verification of published archives and checksums.

An exception records its scope, rationale, risk, compensating control,
approver, creation date, and expiry. Expired exceptions must be renewed or
removed.
