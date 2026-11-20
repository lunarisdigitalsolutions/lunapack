# Design

## Context

See `proposal.md` for motivation and the spec deltas for user-visible behavior.
The existing preference service resolves project, user-project, and global
values. Git network operations span source addition, ref resolution, catalog
discovery, pack and external-source materialization, Git-backed links, and
pack authoring. The Git process runner inherits Git configuration and
environment. Source identity intentionally excludes transport and credentials.

## Goals / Non-Goals

**Goals:**

- Apply one effective preference before every LunaPack-initiated Git network
  operation.
- Enforce the transport decision at both the source boundary and Git process
  boundary so configured URL rewrites cannot silently bypass it.
- Keep project configuration and user-settings documents on their current
  schema version.

**Non-Goals:**

- Add per-source allow-lists or change source identity and lock ownership.
- Change authentication for HTTPS, SSH, or local Git sources.
- Guarantee a credential-free environment for an explicitly enabled plaintext
  request when Git configuration supplies credentials outside credential
  helpers.

## Decisions

1. **Extend existing scoped preferences.** Add the typed preference to the
   existing `luna config` key mapping, persistence model, effective-value
   resolver, and both published schemas. Resolve project, then user-project,
   then global, then `false`; an explicit `false` overrides broader `true`.
   The CLI key remains kebab-case while persisted YAML uses the existing
   camel-case preference shape. A flag or per-source manifest option would
   conflict with the user's selected scope-wide Boolean.

2. **Use a shared transport decision at every network boundary.** Resolve the
   preference and classify each remote URL before ref resolution or any other
   network access. Apply this to source addition, ref resolution, discovery,
   pack and external-source materialization, links, and authoring. Keep
   syntactically valid insecure URLs representable in version-1 configuration;
   reject their network use at runtime so users can inspect or remove legacy
   entries without a migration.

3. **Enforce the decision in Git itself.** Pass per-operation Git configuration
   that denies `http` and `git` protocols for secure-source operations. Enable
   only the protocol required by an explicitly opted-in insecure source. This
   protects against `url.*.insteadOf` rewriting a secure-looking URL to
   plaintext. Overwrite `GIT_ALLOW_PROTOCOL` with a mode-specific allowlist so
   inherited environment state cannot supersede the policy. Remove inherited
   repository-selection environment variables so `-C` selects the intended
   temporary repository. For secure
   operations, resolve the effective URL without connecting, then enforce and
   verify URL-specific redirect rejection and certificate validation on that
   HTTPS destination. This also blocks HTTPS-to-HTTPS redirects; consumers must
   configure the final HTTPS URL. Keep arguments discrete and share one timeout
   and cancellation budget across policy checks and the Git operation. Reject
   query and fragment components because Git URL-specific configuration cannot
   bind security values unambiguously when they are present. Run URL resolution
   and verification in the operation's `-C` repository context, strip only
   Git's line terminator, and reject surrounding whitespace or lossy UTF-8
   decoding.
   The `file` protocol is available only when the originally configured value
   is an absolute local filesystem path. UNC, hosted `file://` values, and a
   network-to-local rewrite fail before Git can access them.

4. **Warn and constrain helper credentials for the exception.** Emit one
   sanitized warning per source per CLI operation before its network access.
   For opted-in HTTP operations, reset inherited `credential.helper` settings
   for that Git process. Do not log full URLs or credentials. This is narrower
   than isolating all Git configuration: embedded URL credentials,
   `http.extraHeader`, and custom proxy configuration may still supply
   credentials and must be disclosed in developer guidance.

   Populate the temporary index without checkout, resolve the effective
   `filter` attribute for every committed path, and reject any configured
   value. This covers nested attributes, macros, inherited attribute files,
   and repository-info attributes. Use an isolated temporary index so this
   verification cannot satisfy or mutate the subsequent real checkout. Set
   `GIT_LFS_SKIP_SMUDGE` defensively. Regular Git blobs remain supported
   without external filters.

5. **Preserve source identity and schema compatibility.** The transport policy
   remains separate from normalized source identity and lock ownership. Add
   `preferences.git.allowInsecureTransport` to the project schema and to global
   and canonical-project user-settings preferences; retain version 1 and
   validate old files unchanged.

## Risks / Trade-offs

- **Explicit opt-in allows network interception and source tampering** →
  warn before every opted-in source use; document migration to HTTPS or SSH.
- **Git configuration can alter the effective protocol or supply credentials**
  → enforce protocol restrictions in Git arguments and the process
  environment, resolve and guard the effective HTTPS destination, test
  `insteadOf` and redirects, suppress credential helpers for plaintext HTTP,
  and document residual `http.extraHeader`, embedded-URL, and proxy credentials.
- **Repository text can be parsed as command options** → reject option-prefixed
  and unsafe scp-style locations before resolution, and terminate Git option
  parsing before every variable repository argument.
- **Git checkout can delegate transport to a smudge process** → inspect
  effective attributes in bounded batches and fail before checkout, rather than
  treating Git subprocess configuration as coverage for filters.
- **The default breaks network access to existing HTTP/Git sources** → keep
  those entries schema-valid, return actionable guidance, and document the
  scoped opt-in and secure-URL migration.

## Migration Plan

Existing version-1 files need no schema migration. Insecure source entries
remain readable, but their network operations fail until
`git.allow-insecure-transport` is enabled in the desired scope. Users should
prefer changing URLs to HTTPS or SSH, then remove or set the preference to
`false`. Rollback requires only setting it to `false` or removing the broader
opt-in; no lock-file rewrite is required.
