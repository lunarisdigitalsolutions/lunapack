# Pack Discovery and Versioned Install

## Why

Configured LunaPack sources currently support only an exact package-path lookup, so users cannot discover available packs or select a package version. A source catalog enables local search and discovery now while defining a source-specific browsing boundary for future source types.

## What Changes

- Add `lunapack search <term>` to find packs whose ID or optional description matches a query, rank results locally by relevance, and display compact package summaries.
- Add `lunapack discover` to enumerate available packages and display one latest-version summary per package ID.
- Recursively enumerate `pack.yml` files in configured local sources, validate each candidate, and treat its containing directory as the pack root.
- Extend `pack.yml` with an optional description and show a maximum 80-character description preview in catalog output when present.
- Extend `lunapack install` to accept `package-id@version`; resolve an exact requested SemVer or the highest available SemVer when omitted.
- Preserve existing install safety: failed resolution or invalid catalog entries do not modify project files or `lunapack.yml`.

## Capabilities

### New Capabilities

- `pack-catalog`: Discover and search packages exposed by configured sources with concise, deterministic command output.

### Modified Capabilities

- `local-pack-lifecycle`: Resolve requested or latest versions from recursively discovered local packs during installation.
- `manifest-schemas`: Permit an optional package description in a schema-valid pack manifest.

## Impact

- Affected CLI code: command registration and output in `projects/cli/src/Lunapack.Cli/CliApplication.cs`; local source enumeration and package resolution in `LocalPackDiscovery.cs`; pack manifest model and schema under `projects/schema/`.
- Affected tests: CLI unit and integration coverage for catalog listing, relevance order, recursive local discovery, malformed candidates, version selection, and current install safeguards.
- Affected documentation: developer CLI and pack-author guidance must document catalog commands, `id@version` syntax, description constraints, and latest-version semantics; internal architecture guidance and an ADR must record the source-specific catalog strategy and deterministic ranking/version-resolution rules.
