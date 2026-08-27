## Why

Pack authors currently need lifecycle scripts even when consumers only need manual setup guidance. Human-readable lifecycle instructions provide that guidance without executing arbitrary code on the consumer machine.

## What Changes

- **BREAKING**: Replace the top-level `scripts` manifest section with a unified `hooks` section. Each lifecycle event contains an ordered list of typed `script` or `instruction` hooks.
- Allow packs to declare multiple mixed hooks for each `preInstall`, `postInstall`, `preUpdate`, and `postUpdate` lifecycle event and process them in declared order.
- Preserve existing script execution, trust, argument templating, safety, suppression, and rollback behavior for hooks whose type is `script`.
- Add Markdown-file hooks whose type is `instruction`.
- Optionally render instruction files with Scriban using the resolved pack parameters available to managed-file templates.
- Present H2 and H3 sections as numbered, confirmation-gated steps; present a document without H2 or H3 headings as one step.
- Add `--skip-instructions` to `luna install` and `luna update`.
- Render complete instructions without confirmation prompts when interactive input is unavailable.
- Replace script-specific pack-authoring commands and catalog output with hook-aware equivalents.
- Keep instructions informational: they execute no code, track no completion state, and add no workflow, link, or code-block semantics.

## Capabilities

### New Capabilities

- `pack-instructions`: Defines instruction rendering, Markdown step detection, guided interactive display, and non-interactive display behavior.

### Modified Capabilities

- `manifest-schemas`: Replaces `scripts` with ordered typed `hooks` declarations and validates script and instruction variants.
- `local-pack-lifecycle`: Processes ordered typed hooks around install and update mutations, preserves script security behavior, and supports explicit instruction suppression.
- `pack-template-rendering`: Makes resolved graph parameters available when an instruction declaration opts into Scriban rendering.
- `pack-authoring`: Replaces script-specific commands with commands that list, add, replace, and remove typed hooks.
- `pack-catalog`: Replaces script-only inspection with ordered hook inspection, including instruction metadata.

## Impact

- Pack manifest schema, manifest models, source materialization, validation, and migration from `scripts` to `hooks`.
- Install and update command options, lifecycle planning, console rendering, and interactive-input handling.
- Pack authoring commands and resolved-pack inspection output.
- Unit and integration coverage for schema validation, rendering, step parsing, lifecycle ordering, skip behavior, and non-interactive execution.
- Product guidance under `docs/product`, consumer and pack-author guidance under `docs/developer`, and maintainer lifecycle guidance under `docs/internal`.
- A new architecture decision record for instruction lifecycle and interaction behavior.
- `CHANGELOG.md`, because pack declarations and install/update output gain externally observable behavior.
- No new runtime dependency is expected; the existing Scriban integration is reused.
