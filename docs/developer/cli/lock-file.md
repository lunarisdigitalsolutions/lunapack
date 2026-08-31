# Lock file reference

`lunapack-lock.yml` is generated resolved state for installed packs and Luna
Links. Luna owns this file. Commit it when your project commits generated lock
state, but do not edit ownership, provenance, or digest fields by hand.

All paths are persisted with `/`. Project targets and local source roots are
non-empty, non-escaping paths relative to the workspace. Pack paths and selected
managed or link source paths are relative to their configured source or
repository base path. Luna rejects a lock file whose effective pack or link
target is rooted or contains a `.` or `..` segment.

This representative local-pack lock shows how identity, ownership, and content
evidence fit together:

```yml
schemaVersion: 1
packs:
  - id: clean-code-guidelines
    version: 1.0.0
    sourceName: local-packs
    sourceIdentity:
      type: local
      path: projects/packs
    sourcePath: projects/packs
    packPath: clean-code-guidelines
    packs: []
    managedFiles:
      - declaredTargetPath: clean-code.md
        targetPath: clean-code.md
        sha256: 0000000000000000000000000000000000000000000000000000000000000000
        strategy:
          type: copy
          method: overwrite
links: {}
```

Real SHA-256 values identify installed bytes; the zero value above is a shape
placeholder, not content evidence to copy into a project.

## Top-level fields

| Field           | Type                  | Required | Meaning                                     |
| --------------- | --------------------- | -------- | ------------------------------------------- |
| `schemaVersion` | Integer; only `1`     | Yes      | Lock schema version.                        |
| `packs`         | Resolved pack array   | Yes      | Installed root and transitive pack records. |
| `links`         | Map of resolved links | No       | Installed Luna Links keyed by link name.    |

Link names use alphanumeric segments separated by single hyphens.

## Resolved pack

Each `packs` item records one exact pack release.

| Field             | Type                         | Required | Meaning                                       |
| ----------------- | ---------------------------- | -------- | --------------------------------------------- |
| `id`              | String                       | Yes      | Resolved pack ID.                             |
| `version`         | Semantic Version string      | Yes      | Exact resolved release.                       |
| `sourceName`      | String                       | Yes      | Configured project source name.               |
| `sourceIdentity`  | Source identity              | Yes      | Source coordinates used for identity checks.  |
| `sourcePath`      | Project-relative path        | Local    | Configured local source root.                 |
| `packPath`        | Relative path                | Yes      | Resolved path to the pack manifest directory. |
| `destination`     | Project-relative path        | No       | Consumer destination applied to this root.    |
| `packs`           | Pack reference array         | Yes      | Exact direct dependencies of this pack.       |
| `managedFiles`    | Managed-file array           | Yes      | Files selected and owned for this pack.       |
| `externalSources` | Map of external source locks | No       | Pack aliases mapped to approved sources.      |
| `gitSource`       | Git provenance               | Git only | Git coordinates and resolved commit.          |

A pack reference contains required `id` and exact `version` fields.

## Source identity and Git provenance

A local source identity contains `type: local` and its source `path`. A Git
source identity contains `type: git`, required `url`, and optional configured
`ref` and repository-relative `path`.

Git provenance contains `type: git`, `url`, and `resolvedCommit`. It also keeps
the configured `ref` and repository-relative `path` when present. A resolved
commit is a 40- or 64-character hexadecimal object ID.

Each entry in `externalSources` is keyed by the alias declared in `pack.yml` and
contains:

| Field            | Type                  | Required | Meaning                                   |
| ---------------- | --------------------- | -------- | ----------------------------------------- |
| `sourceName`     | String                | Yes      | Approved project source used for alias.   |
| `fingerprint`    | `git:`/`local:` ID    | Yes      | Normalized source identity evidence.      |
| `ref`            | String                | Yes      | Canonical Git ref used by the pack alias. |
| `resolvedCommit` | Hexadecimal object ID | Yes      | Exact external Git revision.              |

## Managed file

Each `managedFiles` item records the target that Luna owns and the bytes used to
detect local changes.

| Field                | Type                  | Required    | Meaning                                       |
| -------------------- | --------------------- | ----------- | --------------------------------------------- |
| `declaredTargetPath` | Project-relative path | Yes         | Target declared by the pack before remapping. |
| `targetPath`         | Project-relative path | Yes         | Effective installed target after remapping.   |
| `sha256`             | 64 hex characters     | Yes         | Digest of installed resulting content.        |
| `strategy`           | Type/method object    | No          | Applied copy or merge strategy.               |
| `content`            | Base64 string         | No          | Original section source retained for removal. |
| `sourceAlias`        | String                | Conditional | Pack-defined external source alias.           |
| `sourceName`         | String                | Conditional | Approved project source name.                 |
| `sourceFingerprint`  | `git:`/`local:` ID    | Conditional | Normalized approved source identity.          |
| `sourcePath`         | Source-relative path  | Conditional | Selected path within external source.         |

The four external-source fields are all present or all absent. `content` is
stored for section merges so uninstall can remove the section that pack added.

## Resolved Luna Link

Each `links` value records one installed link.

| Field              | Type              | Required | Meaning                                       |
| ------------------ | ----------------- | -------- | --------------------------------------------- |
| `sourceName`       | String            | Yes      | Configured source name.                       |
| `sourceIdentity`   | Source identity   | Yes      | Local or Git source coordinates.              |
| `definitionSha256` | 64 hex characters | Yes      | Digest used to detect definition changes.     |
| `gitSource`        | Git provenance    | No       | Exact Git revision for a Git-backed link.     |
| `files`            | Link-file array   | Yes      | Selected source and installed target records. |

Each link file requires `sourcePath`, `declaredTargetPath`, `targetPath`, and a
64-character `sha256` digest. Declared and effective targets differ when a
remapping applies.

Use `luna audit` to inspect this evidence and identify modified targets or source
drift. See [Configuration](configuration.md) for portable project intent and
[Luna Links](links.md) for link selection and cache behavior.
