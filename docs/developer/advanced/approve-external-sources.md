# Approve pack-defined external sources

Review and approve additional Git sources when a pack selects external content
that is not already represented by an equivalent workspace source.

## Preview proposed sources

Run the exact pack release as a dry run:

```powershell
luna install application-foundation@1.0.0 `
  --dry-run `
  --scripts skip
```

The plan shows each pack alias, normalized source identity, reused workspace
mapping, proposed source addition, and resulting file action. Check the
repository, canonical ref, base path, and selected targets before approval.

External-source approval is separate from lifecycle-script trust. Approving a
source permits Luna to add source configuration and read selected content; it
does not authorize scripts.

## Choose an approval path

Interactive installation asks once for all conflict-free missing sources. You
can accept or decline the complete proposal.

For reviewed automation, pass `--accept-sources`:

```powershell
luna install application-foundation@1.0.0 `
  --accept-sources `
  --scripts skip
```

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
