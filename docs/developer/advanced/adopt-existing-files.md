# Adopt matching existing files

Use `--adopt-existing` when a pack or Luna Link should own files that already
exist in the project and exactly match the content Luna would install.

## Preview adoption

Start with a dry run:

```powershell
luna install documentation-standard@1.0.0 `
  --adopt-existing `
  --dry-run `
  --scripts skip
```

For a non-merge pack target, Luna renders the selected source first and compares
the resulting bytes with the existing unmanaged file. Luna Links compare the
existing file's SHA-256 digest with the selected source digest. Adoption fails
when any target differs.

Merge strategies already combine source and target content and do not require
`--adopt-existing`.

## Apply ownership

Remove `--dry-run` only after every existing target matches:

```powershell
luna install documentation-standard@1.0.0 `
  --adopt-existing `
  --scripts skip
```

Successful installation records the target and digest in
`lunapack-lock.yml`. Later updates and uninstall operations treat it as managed
content according to its pack strategy or Luna Link lifecycle.

Adoption does not take ownership from another pack or link, and it does not
overwrite a differing unmanaged file. If content differs, compare it with the
pack, reconcile it manually, choose a remapped target, or stop the installation.

Run `luna audit` after installation to verify recorded ownership. See
[Remap managed targets](../remap-targets.md) when the existing file belongs at a
different project path.
