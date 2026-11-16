# Pack catalog layout

A pack catalog may organize releases at any directory depth below its configured
source root. Luna recursively discovers `pack.yml` files and treats each
manifest's directory as that release's root.

## Minimal layout

Small catalogs can place pack IDs directly below the source root:

```text
packs/
  example-quality/
    1.0.0/
      pack.yml
      targets/
```

Keep every published version available. Files referenced by a manifest remain
relative to the version directory containing that manifest.

## Grouped layout

Larger catalogs can add organizational directories before the pack ID:

```text
packs/
  tooling/
    lint-staged/
      integrations/
        example-lint-staged-css/
          1.0.0/
            pack.yml
            fragments/
              package.json/
                example-lint-staged-css.json
  ecosystems/
    dotnet/
      foundations/
        example-dotnet-repository/
          1.0.0/
            pack.yml
```

Use stable families such as an ecosystem, platform, tool, or repository
concern. Add a secondary role such as `components`, `integrations`, `profiles`,
or `foundations` only when it improves browsing. Avoid empty categories and
limit classification depth so release paths remain readable.

Directory categories do not create namespaces and do not affect resolution.
The manifest `id` and `version` remain authoritative, and dependencies continue
to reference pack IDs rather than directory paths. Place a cross-family pack
with its primary discoverable tool or ecosystem, then use tags and dependencies
to expose its other relationships.

## Source boundary

Configure the common catalog root as the source rather than registering each
category separately:

```powershell
luna sources add local engineering ./packs
luna discover
```

This lets dependencies resolve across categories through one source identity.
To publish only part of a catalog, configure that subtree as the source root;
manifests outside it are not discovered.

## Raw and composite packs

A pack is composite when its manifest contains one or more `packs` references;
otherwise it is raw. Directory names do not affect this classification.
`luna discover` and `luna search` show directly installable raw and composite
releases by default. Pass `--only-composites`, or set the corresponding CLI
preference, to hide raw packs.

A manifest can set `directInstallable: false` to reserve a release for use as a
composite dependency. Discover and search always exclude these releases, but
composite resolution, `luna validate`, and `luna inspect` still see them. See
the [`directInstallable` manifest field](manifest.md#direct-installability) for
its default and compatibility limits.
