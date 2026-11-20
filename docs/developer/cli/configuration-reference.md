# LunaPack configuration reference

This reference lists LunaPack project configuration, user settings, sidecars,
preferences, resource limits, and environment variables. Use
[Configuration](configuration.md) for the project-state overview and
[Environment and user settings](environment.md) for cache, profile, and process
lookup behavior.

## Project files

| File                                    | Owner        | Purpose                                                                                  |
| --------------------------------------- | ------------ | ---------------------------------------------------------------------------------------- |
| `lunapack.yml`                          | Project team | Sources, requested packs, variables, remaps, links, preferences, and trust declarations. |
| `lunapack-lock.yml`                     | LunaPack     | Exact resolved graph, source provenance, effective targets, and content digests.         |
| `~/.lunapack/config.yml`                | Current user | Global and canonical-project trust, preferences, and acknowledgements.                   |
| `~/.lunapack/source-ignores.yml`        | Current user | Project and source ignore state.                                                         |
| `~/.lunapack/lifecycle-hook-policy.yml` | Current user | Lifecycle environment warning names and hook timeout policy.                             |

`LUNAPACK_USER_PROFILE` replaces the user profile root for the user-owned
files. It does not move project files or source caches.

## `lunapack.yml`

The project configuration schema requires `schemaVersion`, `sources`, and
`packs`.

| Property        | Type                 | Required | Meaning                                                               |
| --------------- | -------------------- | -------- | --------------------------------------------------------------------- |
| `schemaVersion` | Integer, only `1`    | Yes      | Project configuration schema version.                                 |
| `sources`       | Source array         | Yes      | Local, Git, GitHub, and OCI sources available to the project.         |
| `packs`         | Requested pack array | Yes      | Requested root packs and optional versions, destinations, and remaps. |
| `links`         | Link map             | No       | Luna Link definitions keyed by exact link name.                       |
| `remap`         | Remapping object     | No       | Project-wide directory and file target remappings.                    |
| `preferences`   | CLI preferences      | No       | Project-local CLI preference and resource-limit values.               |
| `trust`         | Trust object         | No       | Project script denial and portable source/pack trust declarations.    |
| `variables`     | Variable map         | No       | Project string, Boolean, or string-array values.                      |

### Sources

Every source has `name` and `type`.

| Type    | Properties                             | Required               |
| ------- | -------------------------------------- | ---------------------- |
| `local` | `path`                                 | `name`, `type`, `path` |
| `git`   | `url`, `ref`, `path`, `timeoutSeconds` | `name`, `type`, `url`  |
| `oci`   | `url`, `path`, `username`              | `name`, `type`, `url`  |

Git `ref` and `path` are optional when a configured ref or repository subdirectory
is not needed. `timeoutSeconds` accepts 1 through 300 and defaults to 300.
OCI `url` must use HTTPS and `username` is non-secret metadata.

```yaml
schemaVersion: 1
sources:
  - name: engineering
    type: local
    path: packs
  - name: upstream
    type: git
    url: https://github.com/example/packs.git
    ref: refs/heads/main
    path: standards
  - name: registry
    type: oci
    url: https://registry.example.com
    path: engineering/packs
    username: reader
packs: []
```

### Requested packs

| Property            | Type                  | Required | Meaning                                     |
| ------------------- | --------------------- | -------- | ------------------------------------------- |
| `id`                | String                | Yes      | Pack ID.                                    |
| `version`           | Semantic version      | No       | Exact release. Omit for catalog resolution. |
| `destination`       | Project-relative path | No       | Consumer destination for this root.         |
| `remap.directories` | Target map            | No       | Directory remapping for this pack entry.    |
| `remap.files`       | Target map            | No       | File remapping for this pack entry.         |

### Remapping

```yaml
remap:
  directories:
    docs: docs/architecture
  files:
    README.md: docs/README.md
```

Keys and values must be project-relative, non-empty, and non-escaping. The exact
effective lock target has precedence over remapping state for later updates.

### Variables

Variable names match `[A-Za-z_][A-Za-z0-9_]*`. Values can be strings, Booleans,
or arrays of strings.

```yaml
variables:
  companyName: Contoso
  generateSamples: true
  platforms:
    - win
    - linux
```

### Luna Links

| Property      | Type                     | Required | Meaning                                            |
| ------------- | ------------------------ | -------- | -------------------------------------------------- |
| `source`      | Source name              | Yes      | Local or Git source selected for this link.        |
| `includes`    | Unique path/glob array   | Yes      | Files or directories selected by the link.         |
| `excludes`    | Unique glob array        | No       | Patterns excluded after includes.                  |
| `path`        | Repository-relative path | No       | Source-relative selector base.                     |
| `target`      | Project-relative path    | No       | Workspace-relative target directory.               |
| `ref`         | String                   | No       | Git ref override.                                  |
| `stripPrefix` | Repository-relative path | No       | Prefix removed from every target.                  |
| `flatten`     | Boolean                  | No       | Place every selected file directly under `target`. |

## Preferences

Boolean preference values use `true` or `false`.

| Key                            | Default | Effect                                    |
| ------------------------------ | ------- | ----------------------------------------- |
| `discover.only-composites`     | `false` | Discover only composite packs.            |
| `search.only-composites`       | `false` | Search only composite packs.              |
| `git.allow-insecure-transport` | `false` | Allow `http://` and `git://` Git sources. |
| `next-steps.enabled`           | `true`  | Show contextual next-step guidance.       |

Preferences in project YAML use this shape:

```yaml
preferences:
  discover:
    onlyComposites: true
  search:
    onlyComposites: false
  git:
    allowInsecureTransport: false
  nextSteps:
    enabled: true
  resourceLimits:
    filesPerPack: 5000
```

Use `luna config set|list|remove` instead of hand-editing project settings.

## Resource limits

Byte limits count raw bytes. Project settings can lower built-in defaults but
cannot raise a stricter user-project or global setting.

| Config key                               | Unit              | Default/project maximum | User ceiling |
| ---------------------------------------- | ----------------- | ----------------------- | ------------ |
| `resource-limits.project-manifest-bytes` | bytes             | 1 MiB                   | 16 MiB       |
| `resource-limits.pack-manifest-bytes`    | bytes             | 1 MiB                   | 64 MiB       |
| `resource-limits.lock-file-bytes`        | bytes             | 64 MiB                  | 1 GiB        |
| `resource-limits.pack-document-bytes`    | bytes             | 16 MiB                  | 256 MiB      |
| `resource-limits.pack-snapshot-bytes`    | bytes             | 2 GiB                   | 16 GiB       |
| `resource-limits.rendered-file-bytes`    | bytes             | 64 MiB                  | 256 MiB      |
| `resource-limits.rendered-total-bytes`   | bytes             | 512 MiB                 | 4 GiB        |
| `resource-limits.dependency-depth`       | edges             | 64                      | 128          |
| `resource-limits.dependency-nodes`       | nodes             | 1,024                   | 100,000      |
| `resource-limits.dependency-references`  | references        | 100,000                 | 1,000,000    |
| `resource-limits.files-per-pack`         | files/directories | 10,000                  | 100,000      |
| `resource-limits.files-per-graph`        | files/directories | 100,000                 | 1,000,000    |
| `resource-limits.oci-archive-entries`    | entries           | 100,000                 | 1,000,000    |
| `resource-limits.oci-download-bytes`     | bytes             | 512 MiB                 | 16 GiB       |
| `resource-limits.oci-entry-bytes`        | bytes             | 512 MiB                 | 4 GiB        |
| `resource-limits.oci-expanded-bytes`     | bytes             | 2 GiB                   | 16 GiB       |

Zero and negative values are invalid. The user ceiling is also a finite upper
bound. The CLI retains additional compiled bootstrap ceilings for project
configuration and user settings.

Project settings are stored under:

```yaml
preferences:
  resourceLimits:
    packManifestBytes: 2097152
    filesPerPack: 5000
```

Global and user-project settings use the matching camel-case names under
`preferences.resourceLimits` in `~/.lunapack/config.yml`.

## Trust and script policy

Project script denial:

```yaml
trust:
  deny:
    scripts: true
```

Portable source and pack trust:

```yaml
trust:
  sources:
    - engineering
  packs:
    - id: dotnet-project
      source: engineering
  scripts:
    allowedEnvironmentVariables:
      - CUSTOM_TOOL_HOME
    timeoutSeconds: 120
```

Omit `trust.scripts` when the default warning allow-list and 300-second timeout
are sufficient. A project script timeout uses the smallest configured project,
project-user, or global-user value.

Global and project-local user policy and trust live in
`~/.lunapack/config.yml`, `<profile>/.lunapack/lifecycle-hook-policy.yml`, and
the source-ignore sidecar. See [Lifecycle hooks and script trust](trust-and-scripts.md).

## `lunapack-lock.yml`

The lock file is generated and should not be edited. Its top-level fields are:

| Property        | Meaning                                          |
| --------------- | ------------------------------------------------ |
| `schemaVersion` | Lock schema version, currently `1`.              |
| `packs`         | Exact resolved root and dependency pack records. |
| `links`         | Resolved Luna Link records.                      |

Pack records retain source identity, exact release, resolved Git or OCI
provenance, effective targets, dependencies, managed-file SHA-256 digests, and
external-source locks. Link records retain source identity, resolved commit,
selected paths, effective targets, and definition digest.

See [Lock file reference](lock-file.md) for every lock property.
