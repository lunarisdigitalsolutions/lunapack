---
status: accepted
date: 2026-08-19
decision-makers: LunaPack maintainers
---

# ADR-0025: Publish Audience-Specific Pitch Decks with Marp

## Context and Problem Statement

The original Marp pitch deck addressed business stakeholders and technical
leaders in one presentation. It mixed organizational outcomes with pack
authoring, consumption, and lifecycle detail, forcing each audience through
material that did not support its decision.

## Decision Drivers

- Give business stakeholders a concise product and adoption narrative.
- Give lead developers and architects relevant technical context.
- Preserve a reviewable Markdown source and repeatable HTML build.

## Considered Options

- Publish separate business and technical decks.
- Keep one deck with mixed audience content.
- Maintain a common deck with audience-specific slide appendices.

## Decision Outcome

Chosen option: "Publish separate business and technical decks", because each
audience needs a focused decision narrative while both decks retain a reviewable
Markdown source and repeatable Marp build.

### Consequences

- Good, because business stakeholders can focus on outcomes, scope, and adoption.
- Good, because technical leaders can focus on pack consumption and authorship.
- Bad, because shared claims must stay aligned across two sources.

### Confirmation

The `build:pitch` script produces versioned HTML output for both documented
Markdown deck sources, and each deck states planned capabilities as planned.

## Pros and Cons of the Options

### Publish Separate Business and Technical Decks

- Good, because each deck can speak directly to its audience.
- Bad, because common product claims appear in two places.

### Keep One Deck with Mixed Audience Content

- Good, because one source avoids duplicate claims.
- Bad, because the presentation remains unfocused for both audiences.

### Maintain a Common Deck with Audience-Specific Slide Appendices

- Good, because shared material stays in a single source.
- Bad, because presenters still must navigate irrelevant content.

## More Information

Marp remains the presentation generator. Audience-specific sources and
artifacts replace a single shared deck.

- [Pitch documentation](../../../pitch/index.md)
- [Business deck](../../../pitch/business-pitch.md)
- [Technical deck](../../../pitch/tech-pitch.md)
- [Pitch build script](../../../../package.json)
