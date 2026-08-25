# Pitch Decks

This directory contains two stakeholder presentations for LunaPack. Both state
planned functionality as planned; neither represents the CLI or lifecycle engine
as shipped software.

## Contents

| File                                   | Audience and purpose                                              |
| -------------------------------------- | ----------------------------------------------------------------- |
| [business-pitch.md](business-pitch.md) | Business stakeholders: problem, outcomes, operating model, scope. |
| [tech-pitch.md](tech-pitch.md)         | Lead developers and architects: consume and author packs safely.  |

## Authoring

Each deck is a self-contained Marp Markdown presentation. Its YAML front matter
enables Marp and defines presentation metadata. The embedded CSS establishes the
color palette, typography, layout, tables, and slide variants. Use `---` to
separate slides and Marp directives such as `<!-- _class: lead -->` to select a
slide variant.

Keep content grounded in the product, architecture, and developer documentation.
Keep the business pitch focused on organizational outcomes and operating model.
Keep the technical pitch focused on the pack lifecycle and the responsibilities
of its consumers and authors.

## Build

Install project dependencies once, then run the repository pitch-build task.
The task writes `business-pitch.html` and `tech-pitch.html` beside their Markdown
sources.

Generated HTML files are versioned presentation artifacts. Rebuild and include
them when changing a deck's Markdown source or styling.
