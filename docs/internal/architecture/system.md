# System

LunaPack manages versioned engineering packs and project-owned file links in
consumer projects through its `luna` .NET CLI. The command layer parses input and selects a workspace;
application services own catalog lookup, graph resolution, planning,
transactions, and state persistence.

The project holds declared intent in `lunapack.yml` and resolved state in
`lunapack-lock.yml`. The configuration is portable; the lock document is generated
evidence of selected packs, source provenance, dependency edges, effective
targets, rendered-content digests, and resolved link selections.

The CLI source groups behavior by project initialization, sources, local pack
authoring, catalog, pack lifecycle, audit, and schema-backed persistence.
`PackManifestStore` owns local `pack.yml` loading, typed mutation, complete-model
validation, and same-directory atomic replacement. Authoring handlers parse
intent and render results; they do not resolve catalogs, install packs, or run
lifecycle scripts. A same-directory exclusive lock file serializes Luna writer
processes; failed lock acquisition leaves `pack.yml` unchanged.

Unit tests cover isolated behavior with an abstract filesystem. Integration
tests invoke the built CLI in temporary projects with a real filesystem.

The source boundary is consumer-owned. Pack manifests select exact pack IDs and
versions, never sources. Project link definitions select regular files from a
configured local or Git source. Pack graphs and resolved links become explicit
managed roots before shared ownership, planning, audit, and transaction logic.
Local and Git resolution retain source provenance in lock state.
