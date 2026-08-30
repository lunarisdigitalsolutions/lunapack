# Pack Ecosystem Working Concept

This working document proposes how LunaPack packs should be scoped, named,
composed, trusted, and evolved. It describes product direction, not an
implemented contract. Current behavior is identified where it constrains a
proposal.

## What LunaPack is

LunaPack is a repository ownership and lifecycle management system.

A pack declares repository content and lifecycle behavior. LunaPack records
where that content came from, what the pack owns, and enough state to reason
about later updates, audits, and removal. Its purpose is not merely to create a
repository. Its purpose is to keep repository policy understandable as both the
pack and the repository evolve.

The core product promise is:

- ownership tracking for pack-managed repository content;
- correct, reviewable updates;
- safe uninstall behavior;
- visible conflicts and overlaps;
- trusted source identity and release provenance; and
- auditable lifecycle decisions.

This distinguishes LunaPack from adjacent tools:

| Tool                     | Primary outcome                                   | LunaPack difference                                                               |
| ------------------------ | ------------------------------------------------- | --------------------------------------------------------------------------------- |
| Template generator       | Create an initial file tree                       | Retains ownership and lifecycle state after creation                              |
| Package manager          | Resolve and install runtime or build dependencies | Manages repository content and its evolution, not application dependencies        |
| Repository starter       | Produce a preferred initial repository shape      | Supports ongoing update, audit, and uninstall workflows                           |
| Configuration management | Enforce machine or service state                  | Operates on version-controlled repository content with consumer-visible ownership |

LunaPack may use templates, dependency resolution, and structured merges to
deliver this promise. Those are supporting mechanisms, not independent product
identities. It should not become a general-purpose package manager, deployment
system, or enterprise policy platform.

## Lifecycle guarantees

A consumer should be able to answer these questions before installation and
throughout a pack's lifetime:

1. Which repository capabilities does this pack claim?
2. Which files, sections, or structural nodes can it own?
3. Which source and exact release supplied each contribution?
4. Which parameters and lifecycle behaviors form its public contract?
5. What will update or uninstall change, preserve, or refuse to change?

These questions define the quality bar. Composition and catalog breadth are
valuable only when they preserve clear answers.

## Pack scope and taxonomy

Primitive, adapter, profile, and foundation remain useful authoring concepts:

| Concept    | Typical responsibility                               | Example                        |
| ---------- | ---------------------------------------------------- | ------------------------------ |
| Primitive  | One independently useful capability                  | `husky`, `markdownlint-config` |
| Adapter    | A connection between capabilities                    | `husky-lint-staged`            |
| Profile    | A tested policy set for one concern or ecosystem     | `lint-staged-dotnet-quality`   |
| Foundation | An opinionated repository baseline spanning concerns | `dotnet-repository`            |

These are guidance, not mandatory manifest types. Reasonable authors may
classify the same pack differently: `lint-staged-quality` can be read as a
profile or as an adapter collection, while a small `dotnet-repository` can be a
profile before it grows into a foundation. Enforcing one category would add
friction without improving ownership correctness.

Use these terms in authoring guidance and optionally as catalog tags. Review
scope from declared capabilities, targets, children, parameters, and lifecycle
behavior instead of trusting a category label.

Atomic means one coherent capability, not one file. Split a pack when either
part is independently adoptable, has a different compatibility schedule, or
requires different ownership or trust policy.

## Naming and scope control

Use lowercase hyphen-separated IDs. Build names from stable concepts:

```text
<ecosystem>-<capability>[-<variant-or-scope>]
```

Apply these rules in order:

- Prefix ecosystem-specific packs with the ecosystem: `dotnet-*`, `github-*`,
  `azure-*`, `node-*`, or `vscode-*`.
- Start tool-owned packs with the canonical tool name when the capability is
  portable: `husky`, `lint-staged`, `markdownlint-config`.
- Name adapters `<producer>-<consumer>` or `<tool>-<tool>`:
  `husky-lint-staged`.
- Add a technology before a profile suffix: `lint-staged-dotnet-quality`, not
  `dotnet-lint-staged` when lint-staged remains the primary discoverable tool.
- Use `config` for one configuration contract, `instructions` for agent
  guidance, `workflow` or `actions` for GitHub automation, `quality` for a
  focused validation profile, `baseline` for a policy set, and `repository` for
  a broad ecosystem foundation.
- Include compatibility facts that select tested behavior, such as
  `dotnet-sdk-10`. Do not parameterize a compatibility boundary merely to
  shorten the catalog.
- Avoid organization names in portable pack IDs. Source identity should carry
  publisher authority.
- Avoid vague suffixes such as `common`, `standard`, `default`, `utils`, and
  `all`. They do not describe ownership or scope.

Names should optimize search, not encode the entire dependency graph. Tags and
inspection output should expose secondary concepts.

Umbrella names such as `github-open-source-baseline` and
`dotnet-library-repository` create a standing risk: their broad names make each
new policy look in scope. Treat any of these conditions as a decomposition
review trigger:

- the pack claims more than one independently adoptable capability;
- it spans more than two policy domains, such as build, security, and
  contribution governance;
- it has more than seven direct child packs;
- a child has a different maintainer, trust boundary, or release cadence; or
- consumers commonly request one part without the rest.

These thresholds prompt review rather than invalidate a pack. A broad pack may
remain useful when it represents a named, tested baseline, but its children
must remain independently inspectable and its scope must not expand by default.

## Pack directory structure

`templates/` is too generic once a pack contains whole files, JSON fragments,
scripts, instructions, and examples. Prefer directories that describe purpose
and target behavior:

```text
projects/packs/lint-staged-css/
  pack.yml
  fragments/
    package.json/
      lint-staged-css.json
  instructions/
    install.md
  scripts/
    verify.ps1
  fixtures/
    minimal/
      package.json
```

Use these conventions:

| Directory             | Content                                               |
| --------------------- | ----------------------------------------------------- |
| `targets/`            | Complete files, mirroring their default project path  |
| `fragments/<target>/` | Inputs merged into a shared target, grouped by target |
| `instructions/`       | Lifecycle guidance displayed to consumers             |
| `scripts/`            | Minimal, reviewable lifecycle programs                |
| `fixtures/`           | Isolated authoring and lifecycle test workspaces      |
| `examples/`           | Optional consumer examples not installed by the pack  |

A complete workflow would live at
`targets/.github/workflows/pull-request-validation.yml`. A JSON contribution
would live at `fragments/package.json/lint-staged-css.json`. An EditorConfig
section would live at
`fragments/.editorconfig/dotnet-csharp.editorconfig`.

The manifest remains the only installation authority. Folder names explain
intent; they do not imply automatic selection. Small packs should omit empty
directories.

### Version layout

Published releases must be immutable and remain resolvable while consumers
hold exact lock references. A source should support this layout:

```text
packs/
  husky/
    1.0.0/
      pack.yml
      fragments/
    1.1.0/
      pack.yml
      fragments/
```

Current catalog discovery already accepts nested manifest directories and can
select among versions. Authoring and release tooling should make this layout
easy and prevent edits to a published release. Replacing `1.0.0` in place with
`1.1.0` makes uninstall hooks and graph-wide updates unable to resolve the
locked release, as the dogfood migration demonstrated.

## Public pack contracts

A pack version communicates the stability of its consumer-facing contract, not
only the bytes it installs. The public contract includes:

- parameter names, types, allowed values, and defaults;
- declared capabilities;
- default targets and supported remaps;
- exposed or generated outputs on which consumers rely;
- child pack versions and meaningful execution order;
- lifecycle hooks, their scope, and expected external effects; and
- ownership and conflict behavior for each target.

Breaking changes include removing or renaming a parameter, narrowing accepted
values, changing a default with observable effects, removing a capability,
changing ownership mode, adding a newly required interactive decision, or
changing lifecycle behavior in a way that can break automation. Removing a
child or changing the meaning of a narrowly named profile is also breaking.

Adding optional metadata or a backward-compatible target is normally minor.
Fixing installed bytes without changing the contract may be patch-level.
Authors must evaluate the effective graph, not only the edited manifest.

### Maturity metadata

Semantic versions do not tell consumers how much lifecycle evidence exists.
Packs should eventually expose a separate maturity state:

```yml
maturity: preview
```

Proposed values are `experimental`, `preview`, `stable`, and `deprecated`.
Maturity should influence discovery and upgrade decisions but never weaken
version or trust checks. Promotion should require documented install, update,
conflict, audit, and uninstall evidence. Deprecation should identify a
replacement when one exists.

## Composition without proliferation

A composite is justified when it represents a named, tested adoption path with
behavior beyond command-line convenience. Consumers should install multiple
packs directly when they want an uncommon combination or independent lifecycle
control.

Publish a composite only when:

- the combination is repeatedly adopted as one policy;
- order, parameter binding, or conflict coordination matters;
- maintainers test the combined install, update, and uninstall lifecycle; and
- the name communicates a stable contract distinct from its children.

Do not publish every combination of tool, ecosystem, and policy. A chain such
as `husky`, `lint-staged`, `husky-lint-staged`, `lint-staged-quality`,
`lint-staged-dotnet-quality`, and `husky-lint-staged-dotnet-quality` should stop
at combinations with demonstrated adoption. The final convenience bundle is
not automatically justified merely because its graph resolves.

Prefer documentation showing a multi-pack install over another composite when
the combination has no coordination logic. Catalog review should reject
composites whose only benefit is reducing the number of install commands.

For accepted composites:

- dependencies use exact versions and declared order;
- a version changes whenever a child version or meaningful order changes;
- graph-visible parameter names are capability-prefixed;
- shared nodes are deduplicated only when effective inputs match; and
- child targets, capabilities, hooks, and ownership remain inspectable.

### Conflicting contributions

Two packs should not rely on accidental last-writer behavior. If two commands
must share one JSON property, publish a coordinator pack that owns the complete
array or add a merge operation capable of composing that value. For example,
Markdownlint and Prettier both target `lint-staged["*.{md,mdx}"]`; the
`lint-staged-markdown` profile owns the ordered command array because today's
JSON merge replaces scalar and array properties at that key.

## Source identity, trust, and provenance

Independent publishers make authority ambiguous before they make merging
difficult. Trust and collision prevention therefore precede advanced
reconciliation on the roadmap. Current pack IDs are graph-global, and
configured source order breaks equal-version selection ties.

### Near-term policy

- Treat `(source fingerprint, pack ID, version)` as release provenance.
- Pin trusted production sources and review source order.
- Reject a resolved graph containing the same ID from different sources unless
  the selected source is explicit.
- Show publisher/source beside every search, install, update, and trust prompt.
- Never let a newly added source silently replace an installed pack's authority.
- Record checksums and exact resolved releases in lock state.

### Future identity model

Add source-qualified references without forcing source names into portable
manifests:

```text
engineering/dotnet-repository@2.0.0
community/dotnet-repository@1.4.0
```

Workspace aliases remain local, while lock state records canonical source
fingerprints. A dependency should resolve within its publisher source by
default. Cross-source dependencies must declare an explicit authority and
require consumer approval. Source aliases must not become globally meaningful
package namespaces.

Add collision diagnostics that compare metadata, dependency graphs, target
claims, and signing or attestation state before installation.

## Capability ownership

File collisions are often symptoms of a higher-level policy conflict. Packs
should be able to declare the repository capabilities they provide or govern:

```yml
capabilities:
  - linting
  - security-scanning
  - release-automation
```

Capability metadata is a proposal. It would let discovery and planning show:

- packs with overlapping responsibilities;
- competing implementations of one capability;
- foundations that silently accumulate unrelated policy; and
- conflicts that do not share a file path.

Capabilities should be stable, curated identifiers rather than arbitrary tags.
They should initially produce diagnostics, not automatic winner selection.
File and structural ownership remain authoritative for mutation.

## Ownership and user edits

Ownership is LunaPack's primary product boundary. Pack updates must distinguish
generated policy from collaborative files, and one strategy cannot safely
govern both.

### Ownership modes

| Mode                    | Appropriate content                           | Update behavior                                                               |
| ----------------------- | --------------------------------------------- | ----------------------------------------------------------------------------- |
| Exclusive file          | Generated workflow or fixed policy            | Replace only when locked base still matches, otherwise require reconciliation |
| Managed section         | Ignore rules or bounded text policy           | Replace only the pack's marked section                                        |
| Structural contribution | JSON, YAML, XML, TOML, or INI node            | Reconcile only owned keys or nodes                                            |
| Seed                    | Contribution guide or community document      | Create once, then relinquish ownership                                        |
| Overlay                 | Local customization layered on a managed base | Recompute base plus retained local overlay                                    |

Current copy, line, section, and JSON strategies cover part of this model.
JSON uninstall intentionally retains merged targets, which protects unrelated
content but cannot remove only the pack's former keys.

Update and uninstall must state what they changed, preserved, retained because
ownership was uncertain, or refused because local edits conflict. The lock
should eventually identify owned structural regions, not only a whole-file
digest. Until a format has reliable region identity and round-trip behavior,
LunaPack should prefer visible conflicts and retained content over destructive
guesses.

## Structural reconciliation research

Three-way, format-preserving structural reconciliation is an experimental,
long-term research area. It is not a solved LunaPack capability or a near-term
guarantee.

JSON, YAML, XML, TOML, and INI differ in comments, duplicate keys, ordering,
anchors, namespaces, schema conventions, and serializer behavior. Ordered
arrays add domain semantics that generic merge algorithms cannot infer. Even
JSON Pointer ownership does not by itself define safe array updates or
uninstall behavior.

A possible three-way model retains the installed base, current local content,
and incoming content. That model still requires format-specific identity,
conflict rules, round-trip fidelity, accepted-local-override state, and
rollback evidence. Each format needs a separate prototype and lifecycle test
matrix before product commitment.

Near-term behavior should remain conservative:

- support deterministic strategies already proven by tests;
- expose owned regions and unresolved conflicts in plans and audits;
- avoid rewriting comments or formatting without an explicit contract;
- retain content rather than delete uncertain ownership; and
- provide format-safe template encoders such as `json.string` before adding
  broader merge promises.

YAML or XML prototypes may be valuable, but source trust, provenance,
collision prevention, immutable releases, and lifecycle correctness have
higher adoption priority.

## Monorepo model

A monorepo often needs one pack release installed several times with different
parameters and destinations. Explicit named instances and target roots are the
preferred direction because current root identity by pack ID cannot express
those instances cleanly.

### Proposed installation instances

Give each requested root an optional local instance name and base target:

```yml
packs:
  - id: dotnet-project
    version: 2.0.0
    instance: billing-api
    targetRoot: services/billing
    variables:
      solutionPath: Billing.slnx
  - id: dotnet-project
    version: 2.0.0
    instance: identity-api
    targetRoot: services/identity
    variables:
      solutionPath: Identity.slnx
```

Explicit instances make ownership, parameters, remaps, hooks, and removal
addressable. Resolve parameters per root instance, then deduplicate only graph
nodes whose effective parameters, target roots, remaps, source, and hook policy
match. Lock ownership keys become `(instance, pack, declared target)` rather
than only pack and target.

Support selectors such as `services/*/project.pack.yml` only after explicit
instances work. Implicit directory discovery makes state and removal harder to
reason about. Workspace-level packs, such as root EditorConfig or CI policy,
remain separate instances at the repository root.

Hooks need scope rules. A package restore may run once per project instance;
repository setup should run once per workspace. Manifests should declare
`hookScope: instance|workspace`, with workspace hooks deduplicated by exact
release and rendered arguments.

## Portfolio policy

Catalog quality matters more than catalog size. LunaPack should maintain a
small number of packs with strong lifecycle evidence before expanding into new
ecosystems.

A candidate pack belongs in the maintained portfolio only when it has:

- a clear capability and ownership boundary;
- real consumer or repository evidence;
- named maintainers and source provenance;
- install, repeated-install, update, conflict, audit, and uninstall tests;
- documented local-edit behavior; and
- a maturity level justified by that evidence.

Candidate lists should live in planning artifacts, not this architecture
concept. Growth targets based on pack count would reward speculative templates
and convenience composites instead of lifecycle quality.

## Prioritized roadmap

### Near term

1. Define ownership and lifecycle guarantees in CLI plans, locks, audits, and
   public documentation.
2. Preserve immutable releases and resolve exact locked content for update,
   rollback, and uninstall.
3. Strengthen source identity, publisher visibility, provenance, trust, and
   collision prevention.
4. Define public pack contracts, breaking-change rules, maturity metadata, and
   capability metadata.
5. Add graph inspection for targets, ownership, capabilities, parameters,
   hooks, source identity, and conflicts.
6. Add fixture-driven pack lifecycle tests and conservative seed ownership.
7. Add format-safe template encoding.

This sequence improves adoption without requiring LunaPack to become a package
registry or general configuration platform.

### Later, after lifecycle evidence

- Add explicit named monorepo instances and scoped hooks.
- Prototype one structural format at a time with round-trip and rollback tests.
- Explore three-way reconciliation and persistent local overrides only after
  per-region ownership is reliable.
- Add publisher verification and release attestations where enterprise trust
  requirements justify them.
- Consider catalog federation and organization allowlists without making a
  central registry mandatory.

## Author checklist

Before publishing a pack:

- Name one coherent capability and ownership boundary.
- Declare capabilities and identify overlaps with existing packs.
- Treat taxonomy as guidance, not proof of scope.
- Use target-oriented source folders.
- Define the public parameter and lifecycle contract.
- Declare exact child versions in intentional order.
- Justify every composite with adoption and combined lifecycle evidence.
- Test absent, matching, conflicting, modified, and remapped targets.
- Test install, repeated install, update, audit, rollback, and uninstall.
- Test skipped scripts and instructions, parameter branches, and shared nodes.
- Preserve every published version and record source provenance.
- Assign maturity from evidence, not aspiration.
- State which local edits are supported and how conflicts surface.

## Decisions to validate next

Prototype these decisions in order:

1. Ownership guarantees and conflict output across plan, lock, audit, update,
   and uninstall.
2. Immutable release storage and exact locked-release resolution.
3. Source-qualified identity, publisher visibility, and cross-source consent.
4. Capability and maturity metadata with diagnostics and discovery behavior.
5. Public pack contract and breaking-change validation.
6. Explicit monorepo instances with target roots and scoped hooks.
7. Format-safe JSON encoding, followed by one experimental structural merge
   prototype.

Each prototype should become an ADR only after its lifecycle, trust,
compatibility, and rollback behavior is demonstrated.
