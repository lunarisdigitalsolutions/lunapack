## Context

See [proposal.md](proposal.md) for motivation and the delta specifications for
observable behavior. The root command currently delegates empty invocation to
System.CommandLine help, while each command handler owns its success and failure
output. Project state already exposes configured sources and requested root
packs, but no component converts that state into workflow guidance.

Guidance crosses project, catalog, and lifecycle handlers. It must not move
catalog resolution or lifecycle policy into the CLI layer, must preserve exit
codes and transactional behavior, and must remain accurate after state-changing
commands. Placeholder input is unavoidable where no concrete pack, keyword, or
repository is known.

## Goals / Non-Goals

**Goals:**

- Derive recommendations from one validated workspace snapshot and the command
  outcome that triggered guidance.
- Keep recommendation selection independent from Spectre.Console rendering.
- Preserve existing command ownership, service boundaries, and failure
  atomicity.
- Make source removal revoke name-bound trust without discarding installed-pack
  evidence.

**Non-Goals:**

- Executing a recommendation, prompting users to choose one, or adding an
  interactive wizard.
- Recommending administrative commands outside the core setup, discovery, and
  pack lifecycle.
- Repairing partially present or schema-invalid project state.
- Persisting guidance history, user preferences, or telemetry.

## Decisions

### Represent guidance as state plus command context

Introduce `INextStepAdvisor` in the CLI application layer. It reads project
state through the existing state store and combines a normalized workspace
stage with a typed command context such as initialization completed, source
added, packs discovered, or pack resolution failed. It returns a summary and an
ordered collection of recommendation values; it does not write state, browse
sources, resolve packs, or run commands.

Handlers request guidance only after their operation has reached a known
successful outcome or a recognized recoverable failure. They pass concrete
identifiers and result counts when available. Error strings are not parsed to
select guidance.

Alternative considered: embed recommendation text in every handler. Rejected
because workspace-stage rules, ordering, limits, and formatting would diverge.

### Classify only valid workspace state

The advisor uses four stages:

1. `NoWorkspace` when neither `lunapack.yml` nor `lunapack-lock.yml` exists.
2. `EmptyWorkspace` when valid project state has no configured sources.
3. `SourcesConfigured` when at least one source exists and no requested root
   pack is installed.
4. `ActiveWorkspace` when at least one requested root pack is installed.

Root summaries report the configured source count and requested root-pack count.
Partially present or invalid state retains its existing validation failure
instead of being classified as a usable workspace. This avoids recommending
`luna init` when initialization would refuse to overwrite an orphaned file.

Alternative considered: infer maturity from filesystem content or the resolved
lock graph. Rejected because configured sources and requested roots are the
portable user-owned workflow state; transient dependencies would inflate the
active-workspace count.

### Give empty root invocation an explicit action

Register a root action that resolves the effective workspace, asks the advisor
for its stage, and renders the corresponding summary and recommendations.
Explicit `--help` and parse errors remain owned by System.CommandLine. Global
workspace selection applies to the root action exactly as it does to
subcommands.

Alternative considered: customize generated help with workflow text. Rejected
because help cannot summarize workspace state and would mix command reference
with next-step guidance.

### Render one bounded recommendation model

Each recommendation contains a short action label and one command string. A
shared renderer emits one `Next step`, `Next steps`, or `Suggested commands`
block, numbers actions when more than one is present, and enforces a maximum of
three in advisor order. Dynamic values pass through the existing safe console
rendering path. Commands use concrete values when known and angle-bracket input
markers only when the workspace cannot supply a usable value.

Guidance follows primary command output and never includes documentation links.
Dry runs do not render state-changing success guidance because workspace state
did not advance. Recommendation rendering does not alter an operation's exit
code.

Alternative considered: write raw multiline strings through each command.
Rejected because numbering, escaping, and the action limit need one contract.

### Remove sources atomically and revoke source-bound trust

`luna sources remove <name>` removes exactly one configured source by its
ordinal name and removes project trust entries bound to that source name in the
same state save. It retains requested roots and immutable resolved lock records
so audit and safe uninstallation remain possible. Later catalog or update
operations continue to apply existing source-availability and source-switch
rules.

Unknown names and failed persistence leave source and trust state unchanged.
After a successful save, guidance is selected from the remaining source count.

Alternative considered: retain trust grants for a removed name. Rejected
because reusing that name could silently transfer trust to a different source.
Automatically uninstalling packs was also rejected because source management
must not trigger lifecycle mutation.

## Risks / Trade-offs

- [Placeholder commands are not immediately executable] → Use concrete pack IDs
  and search terms when available; mark only unknown values with angle brackets.
- [A state reload fails after a successful mutation] → Preserve operation
  success, omit guidance, and emit the state-read diagnostic through existing
  logging rather than reporting a false command failure.
- [Guidance makes snapshot tests brittle] → Test recommendation models and
  renderer structure separately, then keep focused end-to-end assertions for
  each workflow transition.
- [Removing a source leaves installed roots without an available update source]
  → Retain immutable lock evidence and existing explicit source-switch
  protections; document re-adding or switching the source before update.
- [Recommendation blocks become noisy in automation] → Keep them bounded,
  deterministic, and on the normal output channel without prompts or links.

## Migration Plan

1. Add the workspace-stage, recommendation, advisor, and renderer contracts with
   focused unit coverage.
2. Add the root action and command-specific success and recovery integrations.
3. Add atomic source removal with trust cleanup and lifecycle-state retention.
4. Add integration coverage for the guided journey, invalid state, dry runs,
   and redirected output.
5. Add accepted ADRs for the advisor boundary and source-removal trust behavior,
   then update product, internal, and developer documentation.
6. Roll back by removing guidance calls, the root action, and source removal;
   existing project and lock documents require no migration.
