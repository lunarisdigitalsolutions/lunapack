# Luna Links reference

Luna Links copy selected files from a configured local or Git source into the
workspace and track their ownership. Luna `1.1.0` or later is required to read
project or lock files containing links.

## Configuration

`lunapack.yml` stores portable link intent under `links`:

```yml
links:
  agents-csharp-expert:
    source: awesome-copilot
    path: agents
    includes:
      - CSharpExpert.agent.md
    excludes: []
    target: .github/agents
    flatten: false
```

| Field         | Required | Meaning                                                        |
| ------------- | -------- | -------------------------------------------------------------- |
| `source`      | Yes      | Exact case-sensitive configured source name.                   |
| `includes`    | Yes      | Non-empty unique exact files, directories, or glob patterns.   |
| `excludes`    | No       | Glob patterns applied after all includes are combined.         |
| `path`        | No       | Source-relative base for selectors.                            |
| `target`      | No       | Workspace-relative target directory; workspace root if absent. |
| `ref`         | No       | Git ref overriding the configured source ref.                  |
| `stripPrefix` | No       | Complete selected-path prefix removed before target mapping.   |
| `flatten`     | No       | Maps each selected file name directly beneath `target`.        |

Inputs accept `/` or `\`; Luna persists `/`. Paths must be relative and cannot
escape the source or workspace. Includes form a deduplicated ordinal union,
then exclusions apply. Directories recurse. Only regular files are eligible;
symbolic links and reparse points are rejected. Empty selections and duplicate
effective targets fail before mutation.

## Commands

`luna links add <name>` requires `--source` (`-s`) and one or more `--include`
(`-i`) options. It also accepts repeatable `--exclude` (`-e`), `--path`,
`--target` (`-t`), `--ref`, `--strip-prefix`, `--flatten`, `--install`,
`--force`, and `--workspace` (`-w`). Names use alphanumeric segments separated
by single hyphens.

- `luna links list` lists source, effective target, and installation status.
- `luna links show <name>` shows selectors, effective ref, locked commit,
  selected-file count, and locally modified-file count.
- `luna links rm <name>` removes an uninstalled definition.
- `luna links rm <name> --force` removes ownership and unchanged targets while
  preserving modified targets.
- `luna install|update|uninstall <name>` runs the managed-file lifecycle for a
  configured or installed link.
- `luna outdated` and `luna audit` include installed links.

Normal removal refuses installed links and directs users to `luna uninstall`.
Uninstall fails atomically when any owned target is modified.

## Resolved State and Cache

`lunapack-lock.yml` records configured-source identity, canonical definition
digest, effective Git ref and commit when applicable, and each selected file's
source path, declared target, effective target, and SHA-256 digest.

Git link content is cached by configured-source identity and commit:

- Windows: `%LOCALAPPDATA%\LunaPack\cache\sources`
- Linux: `$XDG_CACHE_HOME/lunapack/sources` or `~/.cache/lunapack/sources`
- macOS: `~/Library/Caches/LunaPack/sources`

Cache entries are untrusted generated content. Luna verifies source identity,
commit metadata, blob IDs, and selected bytes before reuse. Project-local pack
catalog metadata under `.lunapack` remains separate.
