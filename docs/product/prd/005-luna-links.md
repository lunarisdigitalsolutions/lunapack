# Luna Links

## User Need

Project teams need selected files from local or Git repositories even when the
source does not publish a LunaPack manifest. They need repeatable updates and
clear ownership without manually copying whole repositories.

## Current Scope

A Luna Link is a project-owned selection of regular files from one configured
source. Includes, exclusions, an optional base path, prefix stripping,
flattening, and a target directory define the copied result. LunaPack records
the definition in `lunapack.yml` and resolved source, target, and SHA-256
evidence in `lunapack-lock.yml`.

Local links snapshot selected bytes before planning. Git links resolve one
immutable commit and materialize only selected blobs. Install, update, outdated,
audit, uninstall, and forced definition removal use the same ownership,
conflict, local-modification, and rollback protections as managed pack files.

## Product Boundaries

Links are copied managed files, not operating-system symbolic links. They are
not publishable packs and do not support dependencies, parameters, templates,
merge strategies, or lifecycle scripts. Links do not introduce provider
plugins or Git cache eviction policy.

Pack catalogs and dependency resolution remain pack-only. A project owns link
intent and selects its configured source; an upstream repository does not need
to adopt LunaPack.
