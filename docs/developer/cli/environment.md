# Environment and user settings reference

Luna reads a small set of environment variables for user settings, cache
placement, and lifecycle command resolution. Project behavior otherwise comes
from CLI input and version-1 YAML documents.

## Luna settings

| Variable                | Platforms | Default              | Behavior                           |
| ----------------------- | --------- | -------------------- | ---------------------------------- |
| `LUNAPACK_USER_PROFILE` | All       | Current user profile | Selects an alternate profile root. |

Luna stores local-user and global-user trust in
`<profile>/.lunapack/config.yml`. A non-empty `LUNAPACK_USER_PROFILE` replaces
the current user profile for this location. It does not change the selected
workspace, source cache, or process environment.
Source-ignore preferences are stored alongside it in
`<profile>/.lunapack/source-ignores.yml`.

Use an isolated profile for automation:

```powershell
$env:LUNAPACK_USER_PROFILE = "$PWD/.ci-profile"
luna trust list
```

Do not share that directory between jobs with different trust boundaries. See
[Scripts and trust](trust-and-scripts.md) for stored scopes and
[Automation](../automation.md) for a complete noninteractive workflow.

## OCI registry authentication

Luna does not expose LunaPack-specific environment variables for OCI usernames,
passwords, tokens, or source selection. It tries anonymous access first. After
an authentication challenge, Luna checks the operating-system credential
store; an interactive command can prompt once and save validated credentials.
Credentials are scoped to the canonical project, source name, registry, and
repository prefix. They are not stored in project files or the Luna user
profile. There are no registry login or logout commands.

Noninteractive generic OCI and GHCR operations need credentials already present
in the runner user's native credential store. The corresponding
[publishing workflow packs](../advanced/publish-private-packs.md) require a
persistent Linux self-hosted runner. They do not consume `GITHUB_TOKEN` or
registry credential environment variables. For ACR, Luna can use Azure
Identity's `DefaultAzureCredential`, including the identity established by
`azure/login` with GitHub OIDC. Azure Identity follows its own configured
credential chain; Luna adds no registry-credential environment provider.

Credential-store availability depends on the operating system. Linux requires
the `libsecret` and GLib runtime libraries and an available Secret Service.
Windows Credential Manager limits the complete stored credential payload to
2,560 bytes. If a native store is unavailable or rejects a credential, Luna
reports that persistence failed and can use the validated credential only for
the current invocation; it never falls back to a plaintext file.

For ACR, Luna can also use Azure Identity's `DefaultAzureCredential` token
exchange when the registry challenges for authentication. Configure an
available Azure identity, such as a developer Azure CLI session or workload
federation in CI, with the required registry permissions. Generic OCI
registries do not invoke Azure Identity.

## Source cache

| Variable         | Platforms | Default    | Behavior                         |
| ---------------- | --------- | ---------- | -------------------------------- |
| `XDG_CACHE_HOME` | Linux     | `~/.cache` | Selects base for Git link cache. |

Linux Git link content is stored below
`$XDG_CACHE_HOME/lunapack/sources`, or `~/.cache/lunapack/sources` when the
variable is empty. Windows uses `%LOCALAPPDATA%\LunaPack\cache\sources`; macOS
uses `~/Library/Caches/LunaPack/sources`. `LUNAPACK_USER_PROFILE` does not move
these caches.

Ordinary Git pack sources use a separate project-local metadata cache below
`<workspace>/.lunapack/git-sources`. They do not keep persistent repository
clones there. Discovery and pack materialization use best-effort-cleaned system
temporary directories; OCI content is not kept in a persistent cache. See
[Understand Git source behavior](../advanced/git-source-behavior.md) for cache
contents, temporary paths, and default-branch reuse.
`luna init` adds `/.lunapack/` to the project-root `.gitignore`, preserving
existing entries.

## Lifecycle command lookup

Luna resolves lifecycle commands with the invoking process's `PATH`. On Windows,
it also uses `PATHEXT`; when `PATHEXT` is absent, `.EXE` is the default extension.
Approved lifecycle processes inherit the invoking process environment, so
remove unneeded credentials before running untrusted hooks.

These host variables affect command lookup; they are not persisted in
`lunapack.yml` or `lunapack-lock.yml`.
