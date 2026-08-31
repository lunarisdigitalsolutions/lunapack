# Lifecycle Hook Safety

Executable lifecycle scripts cross a trust and process boundary. Instruction
hooks display publisher-controlled text but never launch a process. This
reference records what LunaPack protects, what it restores, and what operators
must still accept.

## Boundaries

- Source identity binds trust to a normalized source fingerprint derived from a
  local path or a Git URL, canonical ref, and repository path. A source name
  alone is never authority.
- Local-user, global-user, and project-declared trust remain separate. Project
  declarations require matching local-user acknowledgement before they apply.
- Each scope can set blanket script denial. Any active denial dominates every
  grant and script mode. Evaluation reports project, local-user, then
  global-user origins before resolving commands or requesting confirmation.
- Removing a configured source refuses to proceed while `lunapack-lock.yml`
  still records an installed pack or its external content as a consumer. Once
  no consumer remains, removal clears project source and pack trust bound to
  its name; installed lock evidence remains but grants no authority.
- Typed hooks are planned for every resolved graph node in event and declaration
  order before managed files mutate. Composite event suppression applies to
  scripts and instructions. Transient root trust does not authorize dependency
  scripts.
- Every planned script is authorized before any hook is processed. Instructions
  do not use persisted trust and cannot grant process authority.
- Policy-denied scripts produce one warning per hook before any lifecycle or
  managed-file work. Instructions remain ordered and lifecycle state continues.
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
- Managed state is checkpointed after file mutation and before post hooks. A
  handled failure restores prior files and state; a hard interruption leaves
  lock ownership aligned with the applied mutation.
- Uninstall hooks resolve from exact installed releases. Source-resolution
  failure emits a warning and skips hooks so removal can continue.
- Dry runs prepare and report hooks without launching scripts, prompting for
  trust, or entering guided instruction display. Denied rows report
  `policy-denied` and all origins without execution warnings. `--scripts skip` and
  `--skip-instructions` suppress only their respective hook types.

## Snapshot Object Policy

Operation snapshot roots must not be links or reparse points. During copying,
LunaPack emits a warning and skips child links, reparse points, devices, and
other unsupported entries while retaining regular siblings. Packed hooks or
managed files that depend on a skipped entry will consequently be unavailable.

This policy blocks deterministic link following but is not race-free. Another
process running as the same user can replace an entry between inspection and
copy. ADR-0071 records the implemented boundary and deferred handle-relative
no-follow control.

## Residual Risks

1. **Critical: user-authority execution.** Approved hooks can read credentials,
   modify files, access the network, and start other processes with the user's
   ambient authority.
2. **High: future trusted content.** Source trust spans versions, so a later
   release from a trusted source can execute changed content.
3. **High: irreversible effects.** LunaPack can restore its own state, not
   external writes, remote changes, spawned processes, or deleted credentials.
4. **Medium: skipped source objects.** Unsupported entries are omitted with a
  warning, so a pack may be incomplete even though regular siblings remain.
5. **Medium: settings compromise.** A same-user attacker able to alter user
   settings can change persistent execution authority.
6. **Medium: same-user races.** Object checks and digest verification narrow
  staged-content races but cannot protect every process-visible resource.
7. **Medium: mode trade-offs.** Without persistent denial, `--scripts run`
   bypasses consent for one invocation. Either skip control or policy denial can
   omit setup required by a pack. Resetting denial can reactivate retained
   grants. Prefer prompt mode when reviewing unfamiliar executable content.

LunaPack is not a sandbox or privilege boundary. Treat lifecycle approval as
approval to run publisher-controlled code on the current machine.
