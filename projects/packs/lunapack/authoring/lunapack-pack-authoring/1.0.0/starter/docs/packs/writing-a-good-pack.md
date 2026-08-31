# Write a Good LunaPack Pack

Use this guide to turn a repository policy or reusable setup into a pack with a
clear ownership boundary and predictable lifecycle behavior.

## Start with one capability

Name the repository capability before choosing files. A good pack owns one
coherent outcome, such as a Markdownlint configuration, a pull-request workflow,
or a .NET EditorConfig section. Atomic means independently adoptable, not one
file.

Split capabilities when they have different consumers, maintainers, release
schedules, trust requirements, or uninstall expectations. Compose packs only
when a combination is repeatedly adopted and its order, parameter binding, or
conflict handling needs coordinated lifecycle testing.

Use lowercase hyphen-separated IDs. Prefer stable, searchable names such as
`dotnet-csharp-editorconfig`, `husky-lint-staged`, or
`github-pull-request-workflow`. Avoid vague suffixes such as `common`, `default`,
`utils`, and `all`.

## Organize by purpose

Keep every release immutable under `<pack-id>/<version>/`. Use this layout where
the pack needs each kind of content:

```text
1.0.0/
  pack.yml
  targets/                 # complete files, mirroring default target paths
  fragments/<target>/      # inputs merged into shared targets
  instructions/            # lifecycle guidance shown to consumers
  scripts/                 # minimal lifecycle programs
  fixtures/                # isolated lifecycle test workspaces
  examples/                # examples that the manifest does not install
```

The manifest is the installation authority. Directory names communicate intent
but never select content automatically. Omit unused directories.

## Choose ownership deliberately

Use copy for complete files the pack can safely own. Select an explicit copy
policy for existing targets. Use line merge only when each complete line is an
independent entry, section merge for uniquely marked text regions, and JSON merge
for structural object or array contributions.

Section fragments need unique, pack-specific opening and closing markers. A
pack update replaces only that inclusive region. Test missing, duplicated,
reversed, and locally edited markers.

Prefer a visible conflict over destructive guessing. LunaPack must be able to
explain which source release supplied content, what target it owns, and what an
update or uninstall will preserve, change, or refuse to change.

## Keep contracts stable

Treat parameter names, types, defaults, allowed values, declared targets,
ownership strategies, child versions, hook behavior, and execution order as the
pack's public contract. Pin child packs to exact versions. Version the effective
graph whenever a child version or meaningful order changes.

Use parameters for consumer choices within one compatibility boundary. Publish
a separate pack version or variant when a value selects materially different
tested compatibility behavior.

Never edit a published release in place. Add a new semantic version and keep old
content resolvable for update, uninstall, and rollback operations.

## Minimize lifecycle scripts

Scripts run with the consumer's authority and require trust approval. Use an
instruction when guidance is enough. When a script is necessary, keep it small,
reviewable, deterministic, and explicit about external effects. Pass arguments
as separate manifest values and never embed credentials.

Use `files.path` or `files.relative_path` in template-enabled managed files when
one installed file references another. These functions resolve the effective
target after consumer remapping.

## Validate the lifecycle

Run `luna pack validate` from the release directory before publishing. Then use
an isolated fixture to verify:

- install into an absent target;
- repeated install without unintended changes;
- update from the previous release;
- matching and conflicting existing content;
- supported local edits;
- target remapping;
- script run and skip policies;
- instruction display and skip behavior; and
- uninstall with both unchanged and locally modified targets.

Inspect the dry-run plan and lock state. Confirm exact versions, source
provenance, declared and effective targets, ownership strategies, and hooks are
visible. Publish only when lifecycle behavior is understandable without reading
the implementation.
