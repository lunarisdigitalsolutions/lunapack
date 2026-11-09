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

Use an isolated profile for automation:

```powershell
$env:LUNAPACK_USER_PROFILE = "$PWD/.ci-profile"
luna trust list
```

Do not share that directory between jobs with different trust boundaries. See
[Scripts and trust](trust-and-scripts.md) for stored scopes and
[Automation](../automation.md) for a complete noninteractive workflow.

## OCI registry credentials

| Variable                | Required with         | Behavior                                                              |
| ----------------------- | --------------------- | --------------------------------------------------------------------- |
| `LUNAPACK_OCI_SOURCE`   | Username and password | Selects the exact configured OCI source name for this credential set. |
| `LUNAPACK_OCI_USERNAME` | Source and password   | Supplies the registry username for one source.                        |
| `LUNAPACK_OCI_PASSWORD` | Source and username   | Supplies a token or password for one source.                          |

Supply all three variables together for one configured source. A set selected
for a different source is not reused; an incomplete set for the selected source
fails with an authentication diagnostic. Environment credentials are used only
for the current process and are not saved to the operating-system store.

Without environment credentials, Luna tries anonymous access first. After an
authentication challenge it checks the operating-system credential store and,
in an interactive session, can prompt once for that source. Successfully
validated interactive credentials are saved in Windows Credential Manager,
macOS Keychain, or the Linux Secret Service/keyring. Credentials are scoped to
the canonical project, source name, registry, and repository prefix; they are
not stored in project files or the Luna user profile. There are no registry
login or logout commands.

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

## Lifecycle command lookup

Luna resolves lifecycle commands with the invoking process's `PATH`. On Windows,
it also uses `PATHEXT`; when `PATHEXT` is absent, `.EXE` is the default extension.
Approved lifecycle processes inherit the invoking process environment, so
remove unneeded credentials before running untrusted hooks.

These host variables affect command lookup; they are not persisted in
`lunapack.yml` or `lunapack-lock.yml`.
