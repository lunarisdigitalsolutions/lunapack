---
status: accepted
date: 2026-08-24
decision-makers: LunaPack maintainers
---

# ADR-0044: Render Lifecycle Script Arguments

## Context and Problem Statement

Pack parameters customize managed files, but lifecycle scripts previously
accepted only literal arguments. Authors then needed wrapper scripts or repeated
configuration to pass the same consumer-selected values to setup tools. Script
trust must still apply to the exact process invocation that runs.

## Decision Outcome

Render each lifecycle script argument as a strict Scriban template using the
resolved graph parameters. Render arguments during lifecycle planning, before
trust authorization, dry-run formatting, confirmation, or execution.

`command`, `runner`, and packed `file` remain literal. Each manifest argument
remains one process argument after rendering; no shell command string is built.
An invalid template or unknown variable fails planning before mutation.

### Consequences

- Pack authors can reuse typed install and update parameters in script argv.
- Consent and trust decisions observe the exact rendered invocation.
- Argument templates can use Scriban date functions, matching managed files.
- Literal Scriban delimiters in arguments must be escaped.

### Confirmation

Focused lifecycle-planner tests verify rendered values and fail-closed unknown
variables. Existing executor tests verify arguments remain literal argv values.

## More Information

- [Scripts and trust](../../../developer/cli/trust-and-scripts.md)
- [Use Scriban templates](../../../developer/packs/how-to/use-scriban-templates.md)
- [ADR-0040](0040-secure-lifecycle-scripts-with-scoped-trust.md)
