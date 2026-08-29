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
temporary directories. See
[Understand Git source behavior](../advanced/git-source-behavior.md) for cache
contents, temporary paths, and default-branch reuse.

## Lifecycle command lookup

Luna resolves lifecycle commands with the invoking process's `PATH`. On Windows,
it also uses `PATHEXT`; when `PATHEXT` is absent, `.EXE` is the default extension.
Approved lifecycle processes inherit the invoking process environment, so
remove unneeded credentials before running untrusted hooks.

These host variables affect command lookup; they are not persisted in
`lunapack.yml` or `lunapack-lock.yml`.
