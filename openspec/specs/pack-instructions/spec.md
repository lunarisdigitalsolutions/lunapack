# pack-instructions Specification

## Purpose

Let packs provide parameter-aware manual guidance during install and update lifecycles without executing code or introducing workflow state.

## Requirements

### Requirement: Prepare instruction hooks without executing content

LunaPack SHALL load every applicable instruction hook from the resolved pack snapshot during lifecycle planning. When `templating` is false or omitted, LunaPack SHALL preserve the instruction content literally. When `templating` is true, LunaPack SHALL render the content as a strict Scriban template using that pack's resolved graph parameters before detecting steps. Instruction content SHALL never be interpreted or executed as code. A missing file or template-rendering failure SHALL return a non-success result before any hook executes or project files or state change.

#### Scenario: Preserve a static instruction file

- **WHEN** an applicable instruction hook omits `templating`
- **THEN** LunaPack prepares its Markdown content without Scriban evaluation

#### Scenario: Select conditional instruction content

- **WHEN** a templated instruction uses a resolved boolean or string parameter in a Scriban condition
- **THEN** LunaPack displays only the content produced for the resolved parameter value

#### Scenario: Reject an invalid instruction before mutation

- **WHEN** an applicable instruction file is missing or its enabled Scriban template cannot be rendered
- **THEN** LunaPack returns a non-success result before executing hooks or changing managed files, `lunapack.yml`, or `lunapack-lock.yml`

### Requirement: Detect instruction steps from Markdown headings

LunaPack SHALL interpret H2 headings as top-level steps and H3 headings following an H2 as nested steps until the next H2. Top-level steps SHALL use sequential numbers and nested steps SHALL use the current top-level number plus a sequential child number. An H3 before any H2 SHALL be treated as a top-level step. The heading text SHALL become the step title, and content after that heading through the next H2 or H3 SHALL become its body. Markdown before the first detected step SHALL be displayed once as an unnumbered introduction. A document without H2 or H3 headings SHALL be displayed in full as one untitled top-level step. Step numbering SHALL restart for each instruction hook.

#### Scenario: Detect top-level and nested steps

- **WHEN** an instruction contains H2 `Configure Repository`, H3 `Configure Environment`, and H2 `Run Workflow` in that order
- **THEN** LunaPack produces `Step 1: Configure Repository`, `Step 1.1: Configure Environment`, and `Step 2: Run Workflow`

#### Scenario: Display an instruction without step headings

- **WHEN** an instruction contains no H2 or H3 heading
- **THEN** LunaPack displays the complete document as `Step 1` without a generated title

#### Scenario: Display introductory Markdown once

- **WHEN** an instruction contains Markdown before its first H2 or H3 heading
- **THEN** LunaPack displays that content once before the first numbered step

### Requirement: Guide interactive users through instruction steps

When interactive input is available, LunaPack SHALL display one prepared instruction step at a time in sequence and wait for Enter before displaying the next step. The prompt SHALL not claim that the user completed or validated the described action. Links, code blocks, and other Markdown constructs in a step body SHALL receive no behavior beyond being displayed as instruction content.

#### Scenario: Continue to the next interactive step

- **WHEN** LunaPack displays a step and the user presses Enter
- **THEN** LunaPack displays the next step in the prepared sequence

#### Scenario: Avoid implied task completion

- **WHEN** LunaPack prompts between instruction steps
- **THEN** the prompt asks the user to continue without recording or asserting completion

### Requirement: Keep non-interactive instruction output non-blocking

When interactive input is unavailable, LunaPack SHALL display every prepared instruction hook and all of its steps in sequence without requesting confirmation or failing because input cannot be read.

#### Scenario: Display instructions with redirected input

- **WHEN** an install or update with applicable instruction hooks runs without interactive input
- **THEN** LunaPack displays all prepared instruction content without a confirmation prompt and continues the operation
