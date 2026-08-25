## 1. Advisor And Rendering

- [ ] 1.1 Add workspace-stage and recommendation models plus
      `INextStepAdvisor`, and verify unit tests cover all four valid stages,
      partially present state, invalid state, ordering, and the three-action limit
- [ ] 1.2 Add the shared guidance renderer, and verify formatter tests cover
      singular and plural headings, numbering, placeholder commands, dynamic-value
      escaping, and omission of documentation links
- [ ] 1.3 Wire the advisor and renderer through CLI application composition, and
      verify dependency-focused tests construct every command handler successfully

## 2. Root And Prerequisite Guidance

- [ ] 2.1 Add the empty root-command action with effective-workspace resolution,
      and verify CLI tests cover no workspace, empty workspace, configured sources,
      active workspace, `--workspace`, explicit help, and invalid state
- [ ] 2.2 Add typed recovery guidance for missing workspaces and missing sources
      without parsing error text, and verify focused tests preserve primary errors,
      non-success exit codes, and unchanged state

## 3. Project Setup And Source Removal

- [ ] 3.1 Add post-success guidance to initialization and every source-add
      variant, and verify tests assert persisted-state confirmations and ordered
      recommendations
- [ ] 3.2 Implement `luna sources remove <name>` as an atomic source and
      source-bound project-trust update, and verify tests cover exact-name removal,
      unknown names, failed persistence, unrelated trust retention, installed lock
      retention, and source-name reuse safety
- [ ] 3.3 Add remaining-source and last-source removal guidance, and verify CLI
      tests assert each state-dependent recommendation set

## 4. Catalog And Lifecycle Guidance

- [ ] 4.1 Add result counts and next actions to successful discover, search, and
      inspect output, and verify handler tests cover concrete inspected IDs,
      replacement tokens, ordering, and result counts
- [ ] 4.2 Add search and discovery recovery actions for unresolved inspection
      and installation references, and verify tests preserve existing catalog and
      lifecycle failure atomicity
- [ ] 4.3 Add post-success guidance to install, update, and uninstall, and verify
      tests cover concrete pack IDs, update counts, remaining roots, and last-root
      removal
- [ ] 4.4 Suppress state-changing success guidance for lifecycle dry runs, and
      verify install and update dry-run tests report previews without advanced-stage
      recommendations
- [ ] 4.5 Exercise `luna → init → sources add → discover → install → update` in an
      isolated process test, and verify each successful transition exposes the next
      executable workflow action

## 5. Decisions And Documentation

- [ ] 5.1 Create accepted ADRs for the advisor/rendering boundary and atomic
      source-removal trust invalidation, add them to the ADR index, and verify
      internal documentation links resolve
- [ ] 5.2 Update internal CLI composition and trust/lifecycle guidance, and
      verify it separates implementation ownership from consumer instructions
- [ ] 5.3 Update product CLI and MVP requirements plus developer CLI overview,
      command reference, source management, installation, and update guidance, and
      verify examples match the implemented command output
- [ ] 5.4 Add the root guidance, contextual recommendations, and source removal
      to `CHANGELOG.md`, and verify only externally observable behavior is listed

## 6. Verification

- [ ] 6.1 Run CSharpier and repository formatting checks, and verify no changed
      source or documentation file requires further formatting
- [ ] 6.2 Run focused unit and integration tests plus the full CLI test suite,
      and verify existing commands retain their exit codes and state behavior
- [ ] 6.3 Build the CLI solution and run
      `openspec validate add-contextual-next-step-guidance --type change --strict`,
      and verify both complete successfully
