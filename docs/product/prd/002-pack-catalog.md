# Pack Catalog

## Current Scope

The MVP discovers schema-valid manifests beneath configured local directories
and Git repositories. It selects the latest release per ID for discovery and
groups matching packages with recent releases for search.

Catalog selection uses Semantic Version precedence and configured-source order
for ties. Git entries retain their resolved commit; local entries retain their
project-relative source and pack path.

Hosted catalog metadata, compatibility filters, publisher data, and registry
search belong to the roadmap.
