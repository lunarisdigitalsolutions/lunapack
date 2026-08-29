# Approve pack-defined external sources

Review and approve additional Git sources when a pack selects external content
that is not already represented by an equivalent workspace source.

## Preview proposed sources

Run the exact pack release as a dry run before accepting its proposal. No pack
under `projects/packs` currently declares external Git content. To exercise this
flow against a pack you control, complete
[Use external Git files in a pack](../packs/how-to/use-external-pack-sources.md),
which creates, registers, validates, and previews a disposable example pack.

The plan shows each pack alias, normalized source identity, reused workspace
mapping, proposed source addition, and resulting file action. Check the
repository, canonical ref, base path, and selected targets before approval.

External-source approval is separate from lifecycle-script trust. Approving a
source permits Luna to add source configuration and read selected content; it
does not authorize scripts.

## Choose an approval path

Interactive installation asks once for all conflict-free missing sources. You
can accept or decline the complete proposal.

Luna groups requirements by normalized source fingerprint: repository URL,
canonical ref, and optional base path. Different pack aliases with the same
fingerprint share one checkout and one workspace source. If an equivalent
workspace source already exists, Luna reuses its configured name.

When Luna must add the source, a direct-root alias supplies the proposed name
before a transient dependency alias. Remaining ties use pack ID and alias in
ordinal order. An alias therefore does not guarantee the workspace source name.
Preconfigure an equivalent source when its project-facing name matters.

For reviewed automation, pass `--accept-sources`:

Add it to the reviewed install command from the authoring workflow. Do not use
it before inspecting the dry-run source proposal.

This option accepts only conflict-free additions. It does not bypass Git
authentication, path validation, script trust, transaction handling, or a
source-name conflict.

When a proposed source name already identifies a different source, configure
the required repository explicitly under another valid name, then retry:

```powershell
luna sources add git foundation-assets `
  https://github.com/example/foundation-assets.git `
  --ref REPLACE_WITH_REVIEWED_REF
```

Use the repository and ref printed by Luna instead of the placeholders above.
After installation, `luna audit` reports the approved workspace source,
fingerprint, ref, immutable resolved commit, and selected external files.

See [Understand Git source behavior](git-source-behavior.md) for repository
queries, temporary materialization, caching, and authentication.
