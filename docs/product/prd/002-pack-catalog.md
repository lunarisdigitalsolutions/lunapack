# Pack Catalog

## Current Scope

The CLI discovers schema-valid manifests from configured local directories,
Git repositories, and OCI Distribution repositories. OCI releases use one
validated manifest/config/ZIP artifact contract; direct pack references and
locked releases resolve their known repository without global catalog access.
Registry-wide enumeration remains provider- and permission-dependent.

Catalog selection uses Semantic Version precedence and configured-source order
for ties. Git entries retain their resolved commit; local entries retain their
project-relative source and pack path; OCI entries retain registry, repository,
tag, manifest digest, and content digest. Discovery preserves readable results
when another source is unavailable and reports typed source failures.

Hosted catalog metadata, compatibility filters, publisher identity, and
cross-registry search remain on the roadmap.
