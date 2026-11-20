# `luna sources`

Configure and inspect the local, Git, and OCI pack sources available to a
project.

## Syntax

```text
luna sources add local <name> <path>
luna sources add git <name> <repository-url> [--ref <ref>] [--path <path>] [--timeout-seconds <seconds>]
luna sources add github <name> <organization/repository> [--ref <ref>] [--path <path>]
luna sources add oci <name> <registry-url> [--path <repository-prefix>] [--username <name>]
luna sources list
luna sources remove <name>
luna sources rename <current-name> <new-name>
luna sources ignore <repository-packs>
luna sources unignore <repository-packs>
```

## Source types

| Type     | Required coordinates                                      | Optional coordinates                    |
| -------- | --------------------------------------------------------- | --------------------------------------- |
| `local`  | Name and project-relative path                            | None                                    |
| `git`    | Name and repository URL or absolute local repository path | Ref, repository-relative path, timeout  |
| `github` | Name and `organization/repository`                        | Ref and repository-relative path        |
| `oci`    | Name and normalized HTTPS registry URL                    | Repository prefix and username metadata |

Source names are exact, case-sensitive names. Local paths must remain within
the project. Git URLs and OCI URLs cannot contain credentials, queries, or
fragments. HTTP and Git protocols require the
`git.allow-insecure-transport` opt-in.

## Common options

| Option              | Applies to           | Behavior                                 |
| ------------------- | -------------------- | ---------------------------------------- |
| `--ref`, `-r`       | Git and GitHub       | Select a branch or commit.               |
| `--path`, `-p`      | Git, GitHub, and OCI | Select a repository or registry prefix.  |
| `--username`        | OCI                  | Store non-secret username metadata only. |
| `--timeout-seconds` | Git                  | Override the source probe timeout.       |
| `--workspace`, `-w` | Every subcommand     | Select another project.                  |

## Removal and rename

`luna sources remove` refuses to remove a source that still owns installed pack
or link state. Uninstall consumers before removal. `luna sources rename`
updates the project source name and lock ownership only when the rename leaves
source identity and consumer state valid.

## Ignore state

`luna sources ignore` and `unignore` persist user-local, project-scoped source
state in the `.lunapack` user profile. Ignored sources remain in project
configuration and lock state. The sidecar leaves older strict `config.yml`
readers compatible.

## Examples

```powershell
luna sources add local engineering ./packs
luna sources add git upstream https://github.com/example/packs.git --ref main --path standards
luna sources add oci registry https://registry.example.com --path engineering/packs --username reader
luna sources list
```
