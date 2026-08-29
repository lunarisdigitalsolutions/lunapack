# External documentation audit

This audit evaluates the published LunaPack documentation in `docs/developer`
against the external documentation standard, implemented CLI behavior, public
schemas, tests, and website navigation. It records current coverage, not planned
features.

## Executive summary

The external documentation covers every implemented public command family,
public document type, installation channel, environment setting, and primary
consumer or pack-author workflow. All 36 navigable pages have one dominant
Diataxis purpose and are present in the developer sidebar.

This audit resolved these material gaps:

- Added references for generated lock state and environment-dependent behavior.
- Documented exact line, section, and JSON merge semantics.
- Corrected GitHub source examples that omitted required `--ref` values.
- Corrected supported release targets and removed obsolete MVP wording.
- Added the accepted `luna move` alias to command reference.
- Removed duplicate target-remapping instructions from the install guide.
- Removed maintainer architecture, contribution, and release procedures from
  the external audience.
- Established the external documentation standard and linked it from
  contributor guidance.

No unresolved high- or medium-severity external coverage gap remains. New
commands, schema fields, environment variables, or observable lifecycle
behavior must update this audit when they materially change these findings.

## Page classification

Index pages orient readers and route them to task or reference content. Each
other page has the dominant purpose shown below.

| Page                                             | Dominant purpose | Discovery                      |
| ------------------------------------------------ | ---------------- | ------------------------------ |
| `index.md`                                       | Orientation      | Sidebar root                   |
| `installation.md`                                | How-to           | Get started                    |
| `sources.md`                                     | How-to           | Get started                    |
| `install-packs.md`                               | How-to           | Get started                    |
| `update-packs.md`                                | How-to           | Use packs                      |
| `copy-files-from-git.md`                         | Tutorial         | Use packs                      |
| `parameters-and-variables.md`                    | How-to           | Use packs                      |
| `remap-targets.md`                               | How-to           | Use packs                      |
| `manage-links.md`                                | How-to           | Use packs                      |
| `lifecycle-hooks.md`                             | How-to           | Use packs                      |
| `automation.md`                                  | How-to           | Use packs                      |
| `packs/index.md`                                 | Orientation      | Author packs                   |
| `packs/tutorials/first-pack.md`                  | Tutorial         | Author packs / Tutorials       |
| `packs/tutorials/create-a-pack-version.md`       | Tutorial         | Author packs / Tutorials       |
| `packs/how-to/add-managed-content.md`            | How-to           | Author packs / How-to guides   |
| `packs/how-to/add-conditional-files.md`          | How-to           | Author packs / How-to guides   |
| `packs/how-to/compose-packs.md`                  | How-to           | Author packs / How-to guides   |
| `packs/how-to/use-external-pack-sources.md`      | How-to           | Author packs / How-to guides   |
| `packs/how-to/use-scriban-templates.md`          | How-to           | Author packs / How-to guides   |
| `packs/how-to/use-pack-scripts.md`               | How-to           | Author packs / How-to guides   |
| `packs/how-to/release-a-pack.md`                 | How-to           | Author packs / How-to guides   |
| `packs/explanation/composition-and-lifecycle.md` | Explanation      | Author packs / Concepts        |
| `packs/explanation/ownership-and-safety.md`      | Explanation      | Author packs / Concepts        |
| `cli/index.md`                                   | Orientation      | Reference / CLI                |
| `cli/overview.md`                                | Explanation      | Reference / CLI                |
| `cli/commands.md`                                | Reference        | Reference / CLI                |
| `cli/configuration.md`                           | Reference        | Reference / CLI                |
| `cli/links.md`                                   | Reference        | Reference / CLI                |
| `cli/manifests.md`                               | Reference        | Reference / CLI                |
| `cli/trust-and-scripts.md`                       | Reference        | Reference / CLI                |
| `cli/environment.md`                             | Reference        | Reference / Files and schemas  |
| `cli/lock-file.md`                               | Reference        | Reference / Files and schemas  |
| `packs/reference/manifest.md`                    | Reference        | Reference / Files and schemas  |
| `packs/reference/project-documents.md`           | Reference        | Reference / Files and schemas  |
| `threat-model.md`                                | Explanation      | Sidebar root / Security model  |
| `troubleshooting.md`                             | Reference        | Sidebar root / Troubleshooting |

Long how-to guides may contain the minimum reference facts needed to complete
their task. For example, automation includes process-success behavior because
CI control flow depends on it, and the Scriban guide includes syntax needed to
author a working template. Their task outcomes remain dominant, so splitting
those facts into separate pages would increase navigation cost without creating
a distinct user goal.

## Navigation and audience

The sidebar has seven top-level entries. Every direct child group remains at or
below seven items; the smallest groups contain two closely related pages.
Published pages are reachable within three navigation levels. Every page under
`docs/developer` is in the sidebar; none relies only on an incidental inline
link.

Public documentation contains consumer and pack-author guidance only.
Maintainer architecture, contribution workflow, and release operations live in
`docs/internal`. The security model remains external because users need its
trust boundaries before allowing pack-provided scripts; it is linked from the
landing page, installation and trust guidance, and sidebar root.

## Public surface coverage

### Commands

`cli/commands.md` maps the complete public command tree:

- project initialization, variable, remapping, and managed-file movement;
- source registration, listing, renaming, and removal;
- source and pack trust plus script-denial policy;
- discovery, search, inspection, and validation;
- pack initialization and incremental manifest authoring;
- install, update, outdated, audit, and uninstall lifecycle operations; and
- Luna Link creation, inspection, removal, installation, update, and removal.

It records positional arguments, aliases, required options, repeatability,
defaults, constraints, important option interactions, mutations, dry-run
behavior, and process-success semantics. Task guides provide copyable examples
for workflows where syntax alone is insufficient.

### Documents and environment

| Public surface                                                                 | Authoritative external page   |
| ------------------------------------------------------------------------------ | ----------------------------- |
| `lunapack.yml` source, variable, trust, remap, and link intent                 | `cli/configuration.md`        |
| `lunapack-lock.yml` resolved packs, links, provenance, ownership, and digests  | `cli/lock-file.md`            |
| `pack.yml` metadata, selectors, strategies, parameters, composition, and hooks | `packs/reference/manifest.md` |
| Document ownership and user-edit boundaries                                    | `cli/manifests.md`            |
| User profile, source cache, `PATH`, and `PATHEXT` behavior                     | `cli/environment.md`          |
| Script consent, trust scopes, and persistent denial                            | `cli/trust-and-scripts.md`    |

The generated lock file has a complete field reference and a representative
shape example. Pack-author guides link to exhaustive manifest rules instead of
duplicating them. Persisted paths, path bases, canonical separators, source
identity, and consumer-remapping precedence are explicit.

### Scenarios

The external set covers installation through GitHub releases, npm, NuGet, and
containers; first project setup; local, Git, and GitHub sources; discovery and
version selection; previewed installation; parameters and variables; target
remapping and omission; managed moves; updates and removals; direct Git file
links; noninteractive automation; troubleshooting; and trust boundaries.

Pack authors can create and version a first pack, select managed content, use
all merge strategies, add conditions and templates, compose exact
dependencies, consume approved external Git content, declare lifecycle hooks,
validate releases, and understand ownership and execution order.

Potential topics such as migration from unrelated tools, performance tuning,
or generic dependency-design advice are not current documentation gaps. Luna
does not expose dedicated public behavior for them, and unsupported guidance
would violate the evidence requirement.

## Validation evidence

The re-audit used implementation command definitions and handlers as command
authority, version-1 JSON schemas as document authority, and focused tests for
behavioral edge cases. Final checks produced this evidence:

- Markdown lint passed for all repository documentation.
- The Docusaurus production build completed with link resolution enabled. It
  reported only expected warnings for disabled source-local docs and blog
  directories.
- All 68 focused configuration, lock-file, and pack-manifest schema tests
  passed.
- Generated help confirmed the `move` alias and GitHub source syntax.
- The navigation scan found 36 public pages mapped to 36 unique sidebar IDs,
  with no missing, duplicate, unknown, or link-only pages.
- No public Markdown link targets internal documentation.
- `git diff --check` and repository diagnostics passed after final formatting.

These checks found no public implementation/documentation disagreement.

## Remaining decisions

No durable architecture or governance decision was made by this audit beyond
applying ADR-0022 and the external documentation standard. No new ADR is
required.

Future reviews may split a page when it gains a second distinct audience or
task, but page length alone is not evidence of a Diataxis violation. Likewise,
seven-item sidebar groups remain within the documented navigation bound and
should not gain wrapper levels without a discovery problem.
