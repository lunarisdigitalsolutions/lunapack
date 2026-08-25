---
status: accepted
date: 2026-08-10
decision-makers: LunaPack maintainers
---

# ADR-0010: Adopt Repository-Local Code Review Agent

## Context and Problem Statement

LunaPack needs a repeatable code-review workflow that evaluates changes against its
product goals, architecture principles, governance, developer guidance, and
available specifications. The prior review workflow referenced unavailable
documentation and Azure DevOps work items, so it could not be executed entirely
from this repository.

## Decision Drivers

- Make code review guidance available with the repository.
- Base review conclusions on evidence a reviewer can inspect locally or on GitHub.
- Preserve product, governance, lifecycle, provenance, and documentation review.
- Avoid Azure DevOps dependencies that this repository does not provide.

## Considered Options

- Keep an external review workflow with Azure DevOps work-item validation.
- Use generic Copilot review guidance without repository-specific evidence.
- Provide a repository-local Copilot code review agent.

## Decision Outcome

Chosen option: "Provide a repository-local Copilot code review agent", because it
keeps the workflow executable and review evidence aligned with the repository.

The [Code Reviewer](../../../../.github/agents/code-reviewer.agent.md) resides in
`.github/agents/`. It reads repository product, architecture, governance, developer,
and OpenSpec documentation as applicable. It does not fetch Azure DevOps work items
or verify Azure DevOps acceptance criteria. It posts pull request comments only when
explicitly requested by the user.

### Consequences

- Good, because review guidance is shared, versioned, and discoverable in the agent picker.
- Good, because findings can be grounded in repository or GitHub evidence.
- Bad, because maintainers must keep the agent's documentation links current.
- Bad, because external work-item acceptance criteria are outside this review workflow.

### Confirmation

Verify the agent is discoverable from `.github/agents/`, its documentation links
resolve, and a review uses the stated evidence sources without Azure DevOps access.

## Pros and Cons of the Options

### Keep an External Review Workflow with Azure DevOps Work-Item Validation

- Good, because it can check work-item acceptance criteria where Azure DevOps is available.
- Bad, because the workflow depends on resources outside this repository.

### Use Generic Copilot Review Guidance

- Good, because it needs little maintenance.
- Bad, because it cannot consistently assess LunaPack-specific lifecycle and governance rules.

### Provide a Repository-Local Copilot Code Review Agent

- Good, because repository documentation supplies consistent evidence.
- Bad, because the agent content must evolve with repository conventions.

## More Information

See the [code review agent](../../../../.github/agents/code-reviewer.agent.md),
[governance process](../../governance/process.md), and
[product overview](../../../product/prd/000-product-overview.md).
