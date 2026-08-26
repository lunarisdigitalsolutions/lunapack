## 1. Schema And Manifest Model

- [x] 1.1 Require author and license alongside ID and version, allow empty
      content collections, add optional name and homepage metadata, and verify
      schema tests cover attribution, metadata, legacy, and invalid manifests
- [x] 1.2 Extend pack manifest models and YAML serialization for new metadata and
      empty collections, and verify round-trip tests preserve every supported
      manifest value
- [x] 1.3 Centralize pack schema and semantic validation diagnostics, and verify
      tests return stable manifest locations for multiple simultaneous errors

## 2. Safe Authoring Foundation

- [x] 2.1 Add the local pack-manifest document service for typed load, mutation,
      validation, and serialization, and verify unit tests preserve unrelated
      modeled values
- [x] 2.2 Add same-directory temporary writes, destination-change detection, and
      atomic replacement, and verify failure-injection and concurrent-write tests
      preserve the last complete manifest
- [x] 2.3 Route external authoring paths through `ProjectPath` and add semantic
      glob validation, and verify Windows separators, rooted paths, traversal,
      canonical persisted output, and safe glob cases
- [x] 2.4 Add the terminal interaction boundary for missing initialization input,
      and verify interactive defaults plus redirected-input failures without
      blocking

## 3. Initialization And Inspection

- [x] 3.1 Register the `luna pack` group and `init` command, and verify parser and
      handler tests cover option-only creation, prompts, default version,
      existing-file refusal, invalid input, and selected workspace behavior
- [x] 3.2 Add local `show`, `list`, and `scripts` rendering, and verify snapshots
      cover empty and populated manifests plus safe console escaping
- [x] 3.3 Add local `validate` behavior without source resolution or lifecycle
      effects, and verify valid and invalid manifests produce the specified exit
      codes and diagnostics

## 4. Managed Content Commands

- [x] 4.1 Add file and directory authoring with target, strategy, template, and
      condition options, and verify defaults, explicit values, duplicates, and
      validation failures
- [x] 4.2 Add glob authoring with explicit or unambiguous target handling, and
      verify canonical patterns, ambiguous targets, duplicates, and unsafe input
- [x] 4.3 Add exact managed-selector removal, and verify unique matches succeed
      while missing or ambiguous selectors preserve the manifest

## 5. Script And Reference Commands

- [x] 5.1 Add command-form lifecycle script authoring, and verify every hook,
      ordered arguments, descriptions, duplicate refusal, and explicit
      replacement
- [x] 5.2 Add file-form lifecycle script authoring, and verify runner
      requirements, normalized packed paths, ordered arguments, and mixed-form
      rejection
- [x] 5.3 Add lifecycle script removal and detailed listing, and verify hook
      selection, missing hooks, and unchanged unrelated scripts
- [x] 5.4 Add composite reference list, add, replace, and remove operations, and
      verify exact versions, parameter bindings, disabled hooks, duplicate IDs,
      and missing IDs

## 6. Metadata, Tags, And Parameters

- [x] 6.1 Add scalar metadata `set` and optional metadata removal, and verify all
      supported properties, required-value protection, URI validation, and
      unchanged unrelated fields
- [x] 6.2 Add tag list, add, and remove operations, and verify uniqueness, empty
      values, schema limits, and deterministic ordering
- [x] 6.3 Add parameter list, set, and remove operations, and verify string,
      boolean, and enum declarations, required flags, enum values, prompt
      metadata, invalid names, and missing parameters

## 7. Decisions And Documentation

- [x] 7.1 Create and index an accepted ADR for required pack attribution and
      catalog eligibility, and verify ADR links and status metadata
- [x] 7.2 Update internal CLI composition, persistence, validation, and path
      guidance, and verify implementation ownership remains separate from public
      instructions
- [x] 7.3 Update product requirements for CLI-first incremental pack authoring,
      and verify the supported journey and schema-valid empty-content state
      match implemented behavior
- [x] 7.4 Add a developer pack-authoring tutorial and update command, manifest,
      parameter, reference, and script reference pages, and verify every example
      runs against the implemented CLI
- [x] 7.5 Add the authoring commands, metadata additions, and required
      attribution contract to `CHANGELOG.md`, and verify internal implementation
      details are excluded

## 8. Verification

- [x] 8.1 Run repository C# and documentation formatting checks, and verify no
      changed source or documentation file requires further formatting
- [x] 8.2 Run focused schema, authoring-service, command-handler, and isolated
      process tests plus the full CLI test suite, and verify existing catalog and
      lifecycle commands retain behavior
- [x] 8.3 Build the CLI solution and run strict OpenSpec validation, and verify
      both complete successfully
