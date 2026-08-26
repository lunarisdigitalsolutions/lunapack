# Lifecycle Script Safety

Lifecycle scripts cross a trust and process boundary. This reference records
what LunaPack protects, what it restores, and what operators must still accept.

## Boundaries

- Source identity binds trust to a normalized source fingerprint derived from a
  local path or a Git URL, canonical ref, and repository path. A source name
  alone is never authority.
- Local-user, global-user, and project-declared trust remain separate. Project
  declarations require matching local-user acknowledgement before they apply.
- Removing a configured source refuses to proceed while `lunapack-lock.yml`
  still records an installed pack or its external content as a consumer. Once
  no consumer remains, removal clears project source and pack trust bound to
  its name; installed lock evidence remains but grants no authority.
- Hooks are planned for every resolved graph node before managed files mutate.
  Transient root trust does not authorize dependencies.
- Packed hook files are resolved beneath the copied operation snapshot, hashed,
  and verified immediately before launch. Commands use `ProcessStartInfo` with
  shell execution disabled and literal `ArgumentList` values.
- Exact original `lunapack.yml` bytes are retained. LunaPack verifies and
  restores them after every process, and restores managed files when a
  post-hook or persistence step fails.

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
7. **Medium: mode trade-offs.** `run` bypasses consent for one invocation;
   `skip` can omit setup required by a pack. Prefer `prompt` when reviewing
   unfamiliar content.

LunaPack is not a sandbox or privilege boundary. Treat lifecycle approval as
approval to run publisher-controlled code on the current machine.
