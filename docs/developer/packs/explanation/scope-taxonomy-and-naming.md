# Pack scope, taxonomy, and naming

Use these practices to make packs easier to discover, combine, review, and
maintain. They are authoring guidance, not additional manifest rules. LunaPack
does not require a taxonomy value, and catalog folders do not change pack
identity or resolution.

## Scope one coherent capability

A pack should provide one coherent repository capability. Atomic does not mean
one file: a pull-request validation workflow may need a workflow file,
configuration, and instructions while still representing one capability.

Split a pack when a part:

- is useful on its own;
- has different consumers, maintainers, or release cadence;
- needs a different trust or ownership policy; or
- has independent update or uninstall expectations.

Keep related content together when separating it would hide one lifecycle or
make the pieces unusable alone. Scope is a design judgment, not a validation
threshold.

## Use taxonomy as authoring vocabulary

These terms help authors discuss pack scope without becoming required manifest
types:

| Concept    | Typical responsibility                               | Example ID                               |
| ---------- | ---------------------------------------------------- | ---------------------------------------- |
| Primitive  | One independently useful capability                  | `markdownlint-config`                    |
| Adapter    | A connection between capabilities                    | `husky-lint-staged`                      |
| Profile    | A tested policy set for one concern or ecosystem     | `lint-staged-dotnet-quality`             |
| Foundation | An opinionated repository baseline spanning concerns | `example-dotnet-library-repository`      |

A pack may reasonably fit more than one concept as it evolves. Classify it by
its declared content, dependencies, parameters, and lifecycle behavior rather
than forcing a label into the manifest.

In larger catalogs, group first by a stable family such as ecosystem, platform,
tool, or repository concern. Add a role such as `components`, `integrations`,
`profiles`, or `foundations` only when it improves browsing. Place a cross-family
pack with its primary discoverable owner and expose secondary relationships
through tags and dependencies.

See [Pack catalog layout](../reference/catalog-layout.md) for supported release
layouts and source-boundary behavior.

## Choose searchable pack IDs

Use lowercase, hyphen-separated IDs built from stable concepts:

```text
<ecosystem>-<capability>[-<variant-or-scope>]
```

Prefer these naming patterns:

- Prefix ecosystem-specific packs with the ecosystem, such as `dotnet-*`,
  `github-*`, `azure-*`, `node-*`, or `vscode-*`.
- Start portable, tool-owned packs with the canonical tool name, such as
  `husky` or `markdownlint-config`.
- Name adapters `<producer>-<consumer>` or `<tool>-<tool>`, such as
  `husky-lint-staged`.
- Put a technology before a profile suffix when the tool remains the primary
  discovery term, such as `lint-staged-dotnet-quality`.
- Use `config` for one configuration contract, `instructions` for agent
  guidance, `workflow` or `actions` for GitHub automation, `quality` for a
  focused validation profile, `baseline` for a policy set, and `repository`
  for a broad ecosystem foundation.
- Include a compatibility fact when it selects tested behavior, such as
  `dotnet-sdk-10`.

Avoid organization names in portable IDs; source configuration carries
publisher identity. Also avoid vague terms such as `common`, `standard`,
`default`, `utils`, and `all`. They make ownership and search results harder to
understand.

Names should identify the primary capability, not encode the entire dependency
graph. Use metadata and dependency inspection for secondary concepts.

## Review broad pack scope

Broad profiles and foundations can be useful when they represent a named,
tested adoption path. Review a pack for decomposition when it:

- contains more than one independently adoptable capability;
- spans more than two policy domains, such as build, security, and contribution
  governance;
- references more than seven direct child packs;
- includes a child with a different maintainer, trust boundary, or release
  cadence; or
- regularly attracts requests for only one of its parts.

These are review prompts, not limits. Keep a broad pack when its combined policy
is the useful product, but keep child packs independently inspectable and resist
adding unrelated policy merely because the name is broad.

## Compose only meaningful adoption paths

Publish a composite when the combination is repeatedly adopted as one policy
and order, parameter binding, conflict coordination, or lifecycle testing adds
value. Prefer documenting a multi-pack install when a combination only saves a
few install commands.

For a composite, use exact child versions, intentional declaration order, and a
name that communicates a stable contract distinct from its children. Evaluate
scope and versioning against the effective graph, not only the composite
manifest. See [Composition and lifecycle](composition-and-lifecycle.md) for the
implemented resolution and execution model.
