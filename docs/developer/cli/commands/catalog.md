# Luna catalog and validation commands

Discover, search, validate, and inspect packs from configured sources.

## `luna discover`

```text
luna discover [options]
```

| Option              | Behavior                                                |
| ------------------- | ------------------------------------------------------- |
| `--versions`, `-v`  | Include available releases.                             |
| `--allow-draft`     | Include directly valid draft releases from Git sources. |
| `--only-composites` | Show only composite packs.                              |

## `luna search`

```text
luna search <term> [options]
```

The search term matches pack metadata and identifiers. It accepts the same
`--versions`, `--allow-draft`, and `--only-composites` options as discovery.

## `luna validate`

```text
luna validate <pack-reference> [--format text|json]
```

Validate a pack release selected from configured sources. `--format json`
returns the versioned machine-readable diagnostic envelope.

## `luna inspect`

```text
luna inspect <pack-reference>
```

Display one selected pack manifest and resolved metadata without modifying
project state.

## Exit behavior

Discovery and search return `0` for success, even when no packs match. Source,
pack, schema, or resolution failures return a nonzero operation result.
