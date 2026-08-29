---
applyTo: 'docs/developer/**/*.md'
---

# Developer Documentation

Read the
[external developer documentation standard](../../docs/internal/development/external-documentation-standard.md)
and [website publishing guide](../../docs/internal/development/website-publishing.md).
Treat `docs/developer` as canonical public guidance. Ground claims in implemented
commands, schemas, tests, or maintained examples; never link public pages to
internal documentation.

Keep each page focused on one Diataxis purpose. Update
`projects/website/src/sidebars.js` when discovery or ordering changes, then run
the Docusaurus production build described in the publishing guide.
