# Lifecycle Hook Safety

Executable lifecycle scripts cross a trust and process boundary. Instruction
hooks display publisher-controlled text but never launch a process. This
reference records what LunaPack protects, what it restores, and what operators
must still accept.

## Boundaries

- Source identity binds trust to a normalized local path or Git URL, ref, and
  repository path. A source name alone is never authority.
- Local-user, global-user, and project-declared trust remain separate. Project
  declarations require matching local-user acknowledgement before they apply.
- Removing a configured source also removes project source and pack trust bound
  to its name. Installed lock evidence remains but grants no authority.
- Typed hooks are planned for every resolved graph node in event and declaration
  order before managed files mutate. Composite event suppression applies to
  scripts and instructions. Transient root trust does not authorize dependency
  scripts.
- Every planned script is authorized before any hook is processed. Instructions
  do not use persisted trust and cannot grant process authority.
- Instruction files resolve beneath the copied operation snapshot, decode as
  strict UTF-8, and optionally render with Scriban. The bounded parser recognizes
  only H2 and H3 step headings; displayed links and code blocks gain no behavior.
- Packed hook files are resolved beneath the copied operation snapshot, hashed,
  and verified immediately before launch. Commands use `ProcessStartInfo` with
  shell execution disabled and literal `ArgumentList` values.
- Interactive hooks inherit standard input, output, and error so child prompts
  use the invoking terminal. Noninteractive hooks retain bounded, sanitized
  output capture and must not prompt for input.
- Exact original `lunapack.yml` bytes are retained. LunaPack verifies and
  restores them after every process, and restores managed files when a
  post-hook, instruction cancellation, or persistence step fails.
- Dry runs prepare and report hooks without launching scripts, prompting for
  trust, or entering guided instruction display. `--scripts skip` and
  `--skip-instructions` suppress only their respective hook types.

## Deferred No-Follow Control

Operation snapshots currently follow symbolic links, junctions, mount points,
and other reparse points while copying source content. They do not confine a
same-user source-tree attacker. ADR-0040 records this exception. Do not claim
no-follow snapshot protection until traversal, regular-file validation, and
source-identity checks during copying are implemented and tested.

## Residual Risks

1. **Critical: user-authority execution.** Approved hooks can read credentials,
   modify files, access the network, and start other processes with the user's
   ambient authority.
2. **High: future trusted content.** Source trust spans versions, so a later
   release from a trusted source can execute changed content.
3. **High: irreversible effects.** LunaPack can restore its own state, not
   external writes, remote changes, spawned processes, or deleted credentials.
4. **High: deferred link traversal.** A source can include data from outside
   its apparent tree through links or reparse points before snapshot hashing.
5. **Medium: settings compromise.** A same-user attacker able to alter user
   settings can change persistent execution authority.
6. **Medium: same-user races.** Digest checks narrow staged-content races but
   cannot protect every process-visible resource.
7. **Medium: mode trade-offs.** `--scripts run` bypasses consent for one
   invocation; either skip control can omit setup required by a pack. Prefer
   prompt mode when reviewing unfamiliar executable content.

LunaPack is not a sandbox or privilege boundary. Treat lifecycle approval as
approval to run publisher-controlled code on the current machine.
